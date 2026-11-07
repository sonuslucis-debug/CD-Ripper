# CD Ripper (stage 1)

Reads an audio CD's table of contents, works out the MusicBrainz Disc ID, and shows the
album, artist, track list and front cover. Ripping/encoding come in later stages.

## Getting the .exe (free, no installs)
1. Create a free GitHub account and a new repository (public = unlimited free builds).
2. Upload everything in this folder to it (including the hidden `.github` folder).
3. Open the **Actions** tab -> **Build CdRipper** -> it runs automatically on upload
   (or click **Run workflow**).
4. When it goes green (about 2 minutes), open the run and download **CdRipper-win-x64**
   from *Artifacts*. Unzip it to get `CdRipper.exe`. No installer needed.
