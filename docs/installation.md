# Installation

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

- **Arch Linux:** download the `.pkg.tar.zst` package for your preferred interface and architecture (`x86_64` or `aarch64`), then install it with `pacman`:

```bash
sudo pacman -U ./github-downloader-<version>-<arch>.pkg.tar.zst       # Desktop
sudo pacman -U ./github-downloader-cli-<version>-<arch>.pkg.tar.zst   # CLI
```

Replace `<version>` and `<arch>` with the values in the downloaded filename. Launch the desktop app from your application menu, or run `ghd` for the CLI. Arch packages are available for the desktop app and CLI.

For the Debian/Ubuntu web package, start the packaged service and open [localhost:2008](http://localhost:2008):

```bash
systemctl --user daemon-reload
systemctl --user enable --now github-downloader-web.service
```

The web service downloads and installs on the **server**. It has no authentication and the packaged service listens on all network interfaces. Restrict access; use an authenticated HTTPS reverse proxy for remote access.

## Building from source

Install the .NET 10 SDK and clone with submodules:

```sh
git clone --recurse-submodules https://github.com/Tobias2888/Github-Downloader.git
cd Github-Downloader
dotnet build Github-Downloader.slnx
dotnet test GithubDownloader.Tests/GithubDownloader.Tests.csproj
```

For an existing checkout, run `git submodule update --init --recursive` before building. The projects under `libs/` build FileLib, LoggerLib, SecretsLib, the MetadataGenerator source generator, and KdeNotificationDBusLib from their pinned repositories. Access to private submodules requires GitHub authentication. After pulling changes to submodule versions, run the update command again.

[Usage guide](usage.md) · [CLI reference](cli.md) · [Back to README](../README.md)
