# Installing MediaMan on Windows

MediaMan installs from one file. MPV (the player) and ffprobe (which reads your video files) come with it, so
there is nothing else to install and nothing to configure first. You need Windows 10 or 11 (64-bit) and a folder of
movies or shows.

## 1. Download

Open the [MediaMan releases page](https://github.com/ogredforman/MediaMan/releases) and, under the newest release,
download `MediaMan-Setup-<version>-x64.exe`. Early versions (0.x) are marked *Pre-release*.

To check the download, compare its SHA-256 with the line for it in `SHA256SUMS.txt` on the same release. In
PowerShell: `Get-FileHash .\MediaMan-Setup-<version>-x64.exe`.

## 2. Get past the SmartScreen warning

The installer isn't code-signed yet, so Windows doesn't know who made it and may show **Windows protected your PC**.
That's expected for this build; only continue if you downloaded it from the releases page above.

1. Choose **More info**.
2. Check the file name is `MediaMan-Setup-<version>-x64.exe`, then choose **Run anyway**.

Your browser may also warn that the file "isn't commonly downloaded". Keep it (in Edge: **…** → **Keep** →
**Show more** → **Keep anyway**).

## 3. Install

The installer runs without questions: it installs MediaMan for your Windows account only (no administrator prompt)
into `%LOCALAPPDATA%\Programs\MediaMan`, adds **MediaMan** to the Start menu and the desktop, and starts it when it
finishes.

## 4. First run

MediaMan opens on a three-step setup:

1. **Create the owner account:** a username and a password of at least 12 characters. The owner account controls
   everything in MediaMan; keep the password somewhere safe.
2. **Choose who can connect:** pick **This computer only** to use MediaMan on this PC.
3. **Add your first library:** the folder with your movies or shows (for example `D:\Movies`) and what it contains
   (**Movies and shows**, **Movies**, **Shows** or **Anime**).

Then pick the profile on **Who's watching?**. MediaMan scans the folder and matches each file against TMDB (The
Movie Database) for posters, overviews and cast. A large library takes a while; the posters fill in as it goes.
Titles it isn't sure about wait in **Needs attention** for you to confirm.

MediaMan only reads your library folder. It never renames, moves or deletes your files, and never writes anything
into it, not even artwork or subtitle files.

Press **Play** on a title and it plays inside the MediaMan window. Closing the MediaMan window quits MediaMan.

## 5. Where your data lives

Your data is kept apart from the program, so updating or uninstalling MediaMan doesn't touch it.

| Folder | What's in it |
| --- | --- |
| `%LOCALAPPDATA%\MediaMan` | Your library, matches, accounts, profiles and watch progress: `mediaman.db` (the database), `mediaman.key` (this install's key; it signs sign-ins and encrypts stored secrets, and the database is no use without it), `cache\` (artwork and TMDB answers, rebuilt if lost) and `backups\` (copies of the database taken before each update). |
| `%APPDATA%\MediaMan` | This app's own settings: your sign-in on this PC (encrypted for your Windows account) and the MPV you picked, if any. |
| `%LOCALAPPDATA%\Programs\MediaMan` | The program itself. Replaced by every update; nothing of yours is in it. |

Paste a folder into the File Explorer address bar to open it.

## 6. Back up and restore

A backup is a copy of `%LOCALAPPDATA%\MediaMan`.

1. Quit MediaMan (close its window), and wait a few seconds for it to stop.
2. Copy the whole `%LOCALAPPDATA%\MediaMan` folder somewhere safe. Keep `mediaman.db` and `mediaman.key` together:
   one without the other can't be restored. Don't copy while MediaMan runs; the copy could miss recent changes.

To restore:

1. Quit MediaMan.
2. Rename the current `%LOCALAPPDATA%\MediaMan` (for example to `MediaMan-old`), so none of its files mix with the
   backup's.
3. Copy your backup folder to `%LOCALAPPDATA%\MediaMan`, so `mediaman.db` and `mediaman.key` are directly inside it.
4. Start MediaMan. Once it shows your library, you can delete `MediaMan-old`.

You don't need to back up `%APPDATA%\MediaMan`: on a new PC or Windows account, just sign in again.

## 7. Update

Download the newer installer from the releases page and run it; there's no need to uninstall first. It replaces the
program and keeps your libraries, matches, watch progress and sign-in. MediaMan doesn't update itself yet, so check
the releases page now and then.

Before the new version changes your database, it saves a copy in `%LOCALAPPDATA%\MediaMan\backups` (the three newest
are kept). If the update can't be applied, MediaMan puts your database back as it was, doesn't start, and says which
step failed and where the copy is; reinstall the previous version from the releases page to keep using your library.

Don't install an older version over a newer one: it may not understand the newer database.

## 8. Uninstall

Use **Settings → Apps → Installed apps → MediaMan → Uninstall**. Your data stays; delete `%LOCALAPPDATA%\MediaMan` and
`%APPDATA%\MediaMan` yourself if you want it gone too. The installer also leaves a copy of itself in
`%LOCALAPPDATA%\@mediamandesktop-updater`, which you can delete.

## If something goes wrong

- **"Can't start the MediaMan server"** when MediaMan opens: the message says what happened. If it's about the
  database update, follow its steps. Otherwise restart MediaMan, and reinstall it if it keeps happening.
- **MediaMan opens on a different library, a sign-in you don't know, or a blank or "not found" page:** another
  MediaMan server (for example a second copy you're running) is already using port 8420, and MediaMan connected to
  it. Quit the other one and start MediaMan again.
- **Few or no posters:** matching needs an internet connection to TMDB. Check the connection, then press **Scan** on the
  library in **Settings → Libraries**.
- Anything else: [report it on GitHub](https://github.com/ogredforman/MediaMan/issues) with what you did and what you
  saw.
