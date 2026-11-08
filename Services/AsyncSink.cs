using System.Collections.Concurrent;

namespace CdRipper.Services;

/// <summary>
/// Wraps another sink so encoding happens on its own thread. The drive can keep reading while earlier
/// audio is still being encoded, and several tracks can be encoded at once. Output is identical to
/// using the inner sink directly.
/// </summary>
public sealed class AsyncSink : IPcmSink
{
    readonly IPcmSink _inner;
    readonly Action? _afterFinish;
    readonly CancellationToken _ct;
    readonly BlockingCollection<byte[]> _queue;
    readonly TaskCompletionSource _done = new(TaskCreationOptions.RunContinuationsAsynchronously);

    Thread? _thread;
    long _totalBytes;
    volatile bool _abort;
    volatile bool _sealed;
    volatile Exception? _failure;

    /// <summary>Completes when the file is fully written (and tagged), or faults/cancels.</summary>
    public Task Completion => _done.Task;
    public string FilePath => _inner.FilePath;

    /// <param name="afterFinish">Runs on the worker thread once the file is complete and closed (e.g. tagging).</param>
    public AsyncSink(IPcmSink inner, CancellationToken ct, Action? afterFinish = null, int capacityChunks = 256)
    {
        _inner = inner;
        _ct = ct;
        _afterFinish = afterFinish;
        _queue = new BlockingCollection<byte[]>(capacityChunks);
    }

    public void Begin(long totalPcmBytes)
    {
        _totalBytes = totalPcmBytes;
        _thread = new Thread(Work) { IsBackground = true, Name = "Encoder" };
        _thread.Start();
    }

    public void Write(byte[] buffer, int count)
    {
        ThrowIfFailed();
        var copy = new byte[count];
        Buffer.BlockCopy(buffer, 0, copy, 0, count);
        while (!_queue.TryAdd(copy, 100, _ct))   // waits (briefly, repeatedly) while the encoder catches up
            ThrowIfFailed();
    }

    public void Finish()
    {
        ThrowIfFailed();
        _sealed = true;
        _queue.CompleteAdding();   // the worker finishes the file in the background
    }

    public void Dispose()
    {
        if (_sealed) return;       // finished normally: the worker cleans up after itself
        _abort = true;
        _queue.CompleteAdding();
        if (_thread != null)
        {
            _thread.Join();
        }
        else
        {
            try { _inner.Dispose(); } catch { }
            _done.TrySetCanceled();
        }
    }

    void ThrowIfFailed()
    {
        var failure = _failure;
        if (failure != null) throw new IOException(failure.Message, failure);
    }

    void Work()
    {
        try
        {
            _inner.Begin(_totalBytes);
            foreach (var chunk in _queue.GetConsumingEnumerable())
            {
                if (_abort || _ct.IsCancellationRequested) throw new OperationCanceledException();
                _inner.Write(chunk, chunk.Length);
            }
            if (_abort || _ct.IsCancellationRequested) throw new OperationCanceledException();

            _inner.Finish();
            _inner.Dispose();          // closes the file before anything else touches it
            _afterFinish?.Invoke();
            _done.TrySetResult();
        }
        catch (OperationCanceledException)
        {
            Cleanup();
            _done.TrySetCanceled();
        }
        catch (Exception ex)
        {
            _failure = ex;
            Cleanup();
            _done.TrySetException(ex);
        }
    }

    void Cleanup()
    {
        try { _inner.Dispose(); } catch { }
        try { File.Delete(_inner.FilePath); } catch { }
    }
}
