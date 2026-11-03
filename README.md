# GitHub Downloader

Track GitHub releases, download assets, and keep applications up to date through a desktop app, web interface, or CLI (`ghd`). Supports asset rules, version pinning, local installs, notifications, and configuration import/export.

[Download releases](https://github.com/Tobias2888/Github-Downloader/releases) · [Report issues](https://github.com/Tobias2888/Github-Downloader/issues)

## Documentation

- [Installation](docs/installation.md) — platform setup and web service configuration.
- [Usage guide](docs/usage.md) — repositories, asset rules, installation behavior, and settings.
- [CLI reference](docs/cli.md) — command examples.

Repositories must publish **GitHub Releases with attached assets**. Run only one interface at a time per user: all three share configuration and cache files.

## Building from source

Install the .NET 10 SDK and clone with submodules:

```sh
git clone --recurse-submodules https://github.com/Tobias2888/Github-Downloader.git
cd Github-Downloader
dotnet build Github-Downloader.slnx
dotnet test Github-Downloader.Tests/Github-Downloader.Tests.csproj
```

For an existing checkout, run `git submodule update --init --recursive` before building. The projects under `libs/` build FileLib, LoggerLib, SecretsLib, and the MetadataGenerator source generator from their pinned BonitoCat repositories. After pulling changes to submodule versions, run the update command again.

## License

[GNU GPL v3.0](LICENSE)
