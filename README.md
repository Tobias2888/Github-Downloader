# GitHub Downloader

Track GitHub releases, download assets, and keep applications up to date through a desktop app, web interface, or CLI (`ghd`).

[Download releases](https://github.com/Tobias2888/Github-Downloader/releases) · [Report issues](https://github.com/Tobias2888/Github-Downloader/issues)

## Features

- Manage multiple repositories, follow stable or pre-release channels, and pin versions.
- Select assets manually or with filename rules that follow new releases.
- Download, install, extract, update, reinstall, and review operation history.
- Install local files and manage them in the desktop **Local apps** tab.
- Configure background checks, desktop notifications, download paths, and GitHub tokens.
- Import and export repository configuration.

Repositories must publish **GitHub Releases with attached assets**. Run only one interface at a time per user: all three share configuration and cache files.

## Install

Choose your OS and architecture from [Releases](https://github.com/Tobias2888/Github-Downloader/releases). Builds include the .NET runtime.

- **Windows:** run the installer.
- **macOS:** extract the archive and launch `Github-Downloader`.
- **AppImage:** make it executable and launch it. Install `libnotify-bin` for desktop notifications on Debian/Ubuntu.
- **Debian/Ubuntu:** install the package for your preferred interface:

```bash
sudo apt install ./github-downloader_*.deb       # Desktop
sudo apt install ./github-downloader-cli_*.deb   # CLI
sudo apt install ./github-downloader-web_*.deb   # Web
```

Start the packaged web service and open [localhost:2008](http://localhost:2008):

```bash
systemctl --user daemon-reload
systemctl --user enable --now github-downloader-web.service
```

The web service downloads and installs on the **server**. It has no authentication and the packaged service listens on all network interfaces. Restrict access; use an authenticated HTTPS reverse proxy for remote access.

## Get started

1. Add a repository by GitHub URL or owner/name.
2. Review its asset rule in repository details, or clear the rule to select an asset manually.
3. Choose a download location and click **Update**, or update multiple repositories together.

Asset rules are case-sensitive: `*` matches any text, `?` one character, and `{x64,amd64}` either alias. Separate patterns with spaces or newlines; every inclusion must match, and `!` excludes matches. A rule must select exactly one asset. Example: `*.exe *x64* !*lt12*`. Without a rule, selections follow the saved **asset index**, even when filenames change.

Debian packages install with administrator authorization when `apt-get` is available; otherwise, `.deb` files are saved as ordinary downloads; on systems with `pacman`, `.pkg.tar.zst` and `.pacman` packages install using `pacman -U`; AppImages integrate into the Linux application menu; Windows installers launch interactively. Other files are saved to the download directory. Enable **Auto-extract** for supported archives. **Save file anyway** keeps an additional installer copy. Installed Arch packages can be uninstalled with `pacman -R` from GHD; older entries without a recorded package name ask for it first. Saved installers are kept for reinstalling.

On Linux, installing a local archive automatically extracts it into its own folder under GHD’s data directory (`apps/`) and adds an application-menu entry for its executable. Other platforms still ask for an extraction folder. A single detected executable is selected automatically; otherwise, a file picker opens in the extraction folder. The choice is reused for reinstalls. Cancelling the picker keeps the extracted app without a menu entry. Uninstall removes the generated entry and the entire managed application folder, including files created by the app. Original archives and saved installers are kept. For manually chosen extraction folders, only unchanged app-owned files and empty app subfolders are removed.

Closing the desktop window leaves the app in the tray; use **Quit** to exit. Launch again to show it, or use `--hidden` to start in the tray.

In **Settings → Notifications**, choose alerts once per new release (per app session) or recurring reminders at a configurable interval, default **15 minutes**. Optional quiet hours apply; the desktop app must stay running.

For private repositories or authenticated requests, configure a personal access token in the desktop token manager, web settings, or with `ghd pat set YOUR_TOKEN`. Tokens are stored separately from repository exports. Importing configuration replaces the tracked repositories.

## CLI

Repository and asset IDs are zero-based list positions. Use `ghd --help` for all commands.

```bash
ghd add https://github.com/owner/repository
ghd list repos
ghd list assets 0
ghd repo 0 set-asset 1
ghd repo 0 set-asset-rule "*linux*x64*.AppImage"
ghd check
ghd update --all
ghd reinstall 0
ghd install-local "/path/to/package.deb"
ghd history
ghd --gui
```

## License

[GNU GPL v3.0](LICENSE)