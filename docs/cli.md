# CLI reference

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

[Installation](installation.md) · [Usage guide](usage.md) · [Back to README](../README.md)
