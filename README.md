# Sonus Lucis CD Ripper

A simple, standalone Windows program for ripping audio CDs. It identifies your disc, fetches the album details and cover art, lets you check and edit everything, then rips to MP3, FLAC, OGG or WAV with the tags and artwork already in place.

## Features

- **Automatic album lookup.** Reads the disc's table of contents and finds the album, artist and track titles on MusicBrainz. Front cover art comes from the Cover Art Archive.
- **Edit before you rip.** Album, album artist, year, genre, disc number, every track title and artist, and the cover image can all be changed. Discs that aren't found online can be typed in by hand.
- **Four formats.** MP3 (default, 192 kbps; also V0, V2, 320 and 128), FLAC (lossless), OGG Vorbis (about 128 to 256 kbps) and WAV (uncompressed).
- **Tags and artwork embedded** in MP3, FLAC and OGG files. WAV has no standard tag format, so WAV files carry none.
- **Tidy output.** Files are saved as `Artist - Album\01 - Artist - Title.mp3`, with the cover saved alongside as `cover.jpg`.
- **Optional M3U playlist** for each album (on by default).
- **Live readout.** While ripping, the window shows the current and average speed, elapsed time, read retries and any unreadable sectors.
- **Careful reading.** Unreadable sectors are retried, and any that can't be recovered are reported rather than hidden. The app does not perform AccurateRip verification.
- **One file.** `CdRipper.exe` needs no installer.

## Requirements

- Windows 10 or 11, 64-bit
- A CD drive (a USB drive is fine)
- An internet connection for album and cover lookups

## Using it

1. Insert an audio CD and open `CdRipper.exe`.
2. Choose your drive and click **Read disc**.
3. Check the details, pick the release that matches your disc if several are listed, and edit anything that's wrong. Double-click the cover to see it full size.
4. Choose a folder, a format and a quality, then click **Rip selected tracks**.

## Copy Protection

Sonus Lucis CD Ripper is designed for ripping standard, unprotected audio CDs for personal use.

The software does not attempt to bypass, circumvent or defeat copy-protection mechanisms or other access restrictions.

Please respect applicable copyright laws and the rights of content owners when using this software.

## Building from source

The project builds automatically on GitHub: upload it to a repository and the workflow in `.github/workflows/build.yml` produces `CdRipper.exe` under **Actions**. To build it yourself, install the .NET 8 SDK and run:

```
dotnet publish CdRipper.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

## Credits

- **Created by Sonus Lucis**: concept, design, direction, logos and artwork, and testing.
- **Programmed by Claude**, made by Anthropic: all of the code.
- **Album data:** [MusicBrainz](https://musicbrainz.org).
- **Cover art:** [Cover Art Archive](https://coverartarchive.org).
- **MP3 encoding:** the [LAME](https://lame.sourceforge.io) encoder (LGPL), used through the NAudio.Lame library.
- **OGG Vorbis encoding:** [OggVorbisEncoder](https://github.com/SteveLillis/.NET-Ogg-Vorbis-Encoder) by Steve Lillis (MIT licence). Vorbis and FLAC are open formats from the Xiph.Org Foundation.
- **FLAC encoding:** a lossless encoder written for this program, producing standard FLAC files.
- **Tagging:** [TagLib#](https://github.com/mono/taglib-sharp) (LGPL 2.1).
- **Typeface:** Montserrat, by The Montserrat Project Authors, under the SIL Open Font License 1.1 (see `Fonts/OFL.txt`).

[sonuslucis.com](https://sonuslucis.com)
