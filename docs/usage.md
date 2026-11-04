# Usage guide

1. Add a repository by GitHub URL or owner/name.
2. Review its asset rule in repository details, or clear the rule to select an asset manually.
3. Choose a download location and click **Update**, or update multiple repositories together.

Asset rules are case-sensitive: `*` matches any text, `?` one character, and `{x64,amd64}` either alias. Separate patterns with spaces or newlines; every inclusion must match, and `!` excludes matches. A rule must select exactly one asset. Example: `*.exe *x64* !*lt12*`. Without a rule, selections follow the saved **asset index**, even when filenames change.

Debian packages install with administrator authorization when `apt-get` is available; otherwise, `.deb` files are saved as ordinary downloads; on systems with `pacman`, `.pkg.tar.zst` and `.pacman` packages install using `pacman -U`; AppImages integrate into the Linux application menu; Windows installers launch interactively. Other files are saved to the download directory. Enable **Auto-extract** for supported archives. **Save file anyway** keeps an additional installer copy. Installed Arch packages can be uninstalled with `pacman -R` from GHD; older entries without a recorded package name ask for it first. Saved installers are kept for reinstalling.

On Linux, installing a local archive automatically extracts it into its own folder under GHD’s data directory (`apps/`) and adds an application-menu entry for its executable. Other platforms still ask for an extraction folder. A single detected executable is selected automatically; otherwise, a file picker opens in the extraction folder. The choice is reused for reinstalls. Cancelling the picker keeps the extracted app without a menu entry. Use **Choose icon…** on an installed archive app to select a PNG, SVG, or XPM image for its menu entry. GHD saves a copy of the icon and reuses it for reinstalls and new archive versions. Without a custom icon, GHD uses an archive’s `icon.png` or `icon.svg` when available. Uninstall removes the generated entry and the entire managed application folder, including files created by the app. Original archives, saved installers, and chosen icons are kept for reinstalling. Archives extracted into manually chosen folders have no uninstall action. Local archive entries do not store a list of extracted files.

On Linux, use **Launch arguments…** in repository details or on an installed local AppImage or archive to customize its application-menu command. For example, enter `--disable-gpu --profile "Work profile"`. Arguments are saved across updates and reinstalls; saving an empty value removes them. Quotes group values containing spaces, and a backslash escapes the next character outside single quotes. Arguments are literal values: shell expansion and desktop field codes such as `%f` are not interpreted. Changes apply immediately when a launcher exists; repository options also apply on the next installation.

Closing the desktop window leaves the app in the tray; use **Quit** to exit. Launch again to show it, or use `--hidden` to start in the tray.

In **Settings → Notifications**, choose alerts once per new release (per app session) or recurring reminders at a configurable interval, default **15 minutes**. Optional quiet hours apply; the desktop app must stay running.

On KDE Plasma, desktop operations (downloads, updates, reinstalls, local installs, and uninstalls) also appear as native job notifications. Downloads report progress and speed; extraction reports percentages, and installation phases show status text. KDE’s Cancel button cancels downloads where supported; an installation already in progress finishes first. Disable **Settings → Notifications → KDE progress notifications** to turn off these job notifications independently of update and completion alerts. The preference is saved and applies to new desktop operations. These job notifications also follow the desktop notification master switch and quiet hours. Routine update checks do not create job notifications.

The CLI also reports updates, reinstalls, and local installs to KDE when a session bus is available. KDE notification support is optional: other desktops, headless sessions, and notification-service failures do not prevent operations from running.

For private repositories or authenticated requests, configure a personal access token in the desktop token manager, web settings, or with `ghd pat set YOUR_TOKEN`. Tokens are stored separately from repository exports. Importing configuration replaces the tracked repositories.

[Installation](installation.md) · [CLI reference](cli.md) · [Back to README](../README.md)

## Logs

In **Settings → Logs**, use **Open logs folder** to open the application logs in your file manager. Enable **Limit saved log files** and choose a positive file count, including the current log. Leave the limit off to keep all logs. Changes save automatically; LoggerLib removes older files beyond the limit when the next desktop log is created, usually on the next application start.
