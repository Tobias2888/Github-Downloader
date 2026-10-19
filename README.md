# GitHub Downloader

Track GitHub releases, download the assets you need, and keep your applications up to date from a desktop app, a browser, or the command line.

[Download releases](https://github.com/Tobias2888/Github-Downloader/releases) · [Report a bug](https://github.com/Tobias2888/Github-Downloader/issues/new?template=bug_report.md) · [Request a feature](https://github.com/Tobias2888/Github-Downloader/issues/new?template=feature_request.md)

## Features

- Track multiple repositories and compare their downloaded and available versions.
- Choose a release asset for each repository and follow the latest release or a specific tag.
- Read release notes with Markdown formatting and clickable GitHub issue references.
- Update repositories individually or in bulk, or reinstall an existing version.
- Set download directories and optional filenames for saved assets.
- Install Debian packages, integrate AppImages into the Linux application menu, and launch Windows installers.
- Use a GitHub personal access token for private repositories and authenticated API requests.
- Import and export repository configuration from the desktop or web interface.
- Configure desktop notifications for available updates and completed downloads, including allowed hours.

GitHub Downloader works with files attached to **GitHub Releases**. A repository needs a release with downloadable assets; a source tag alone is not enough.

## Choose an interface

| Interface | Best for | Includes |
| --- | --- | --- |
| **Desktop** | Managing applications on your computer | Avalonia UI, system tray, background update checks, release details, and download progress |
| **Web** | Managing downloads through a browser | Repository management, changelogs, activity logs, settings, and a REST API with Swagger UI |
| **CLI** (`ghd`) | Terminal workflows and scripts | Repository and asset selection, version checks, updates, and reinstalls |

> Run only one interface at a time for the same user. The desktop app, web server, and CLI share repository configuration and a download cache, and separate processes do not coordinate access. Quit the desktop app from its tray menu before using the CLI or web server.

## Installation

Download the package for your operating system and CPU architecture from the [Releases page](https://github.com/Tobias2888/Github-Downloader/releases).

Release builds are self-contained, so they do not require a separate .NET runtime installation. Native platform dependencies still apply. The Linux desktop and CLI Debian packages declare `libsecret-1-0` and `gnome-keyring` as dependencies.

The Linux desktop also uses `notify-send` (`libnotify-bin` on Debian/Ubuntu) for system notifications. The desktop Debian packages install it automatically; AppImage users should install it through their package manager.

### Debian / Ubuntu

In the directory containing your downloaded package, use the appropriate command:

```bash
# Desktop
sudo apt install ./github-downloader_*.deb

# CLI
sudo apt install ./github-downloader-cli_*.deb

# Web
sudo apt install ./github-downloader-web_*.deb
```

Launch the desktop app from your application menu, or run `ghd --gui` with both the CLI and desktop packages installed. Run `ghd --help` to use the CLI.

The web package includes a **systemd user service**. Start it in your user session:

```bash
systemctl --user daemon-reload
systemctl --user enable --now github-downloader-web.service
```

Then open [http://localhost:2008](http://localhost:2008).

To stop it:

```bash
systemctl --user stop github-downloader-web.service
```

### AppImage

Make the downloaded AppImage executable, then launch it:

```bash
chmod +x Github_Downloader-x86_64.AppImage
./Github_Downloader-x86_64.AppImage
```

### Windows and macOS

On Windows, run the installer matching your architecture. It creates application shortcuts and registers the desktop app to start hidden in the tray at sign-in.

On macOS, extract the archive matching your architecture and launch the `Github-Downloader` executable inside it.

## Desktop quick start

1. Add a repository using its GitHub URL or the separate owner and repository fields.
2. Review the default asset rule for your operating system and CPU architecture in **More → Download Settings**. Edit it if needed, or clear it to select a release asset manually. Manual selections are saved by filename, so reordering assets in a release does not change the selected file.
3. Open **More** to read the changelog, choose a release version, and adjust download settings.
4. Click **Update** for one repository, or select repositories and click **Update Selected**.
5. Use **Search for updates** to refresh release information. Background checks and their interval can be configured in **Settings**.

The desktop app opens its window by default using saved repository data while GitHub details refresh in the background. Launching it again brings the running instance's window forward. Add `--hidden` to start only in the system tray, for example `Github-Downloader --hidden` (or `Github-Downloader.exe --hidden` on Windows). Packaged Linux and Windows autostart entries include `--hidden`, so signing in does not open a window. AppImages also accept `--hidden`.

Click the tray icon to show or hide the window. Closing the window keeps the app running; use **Quit** in the tray menu to exit completely. Launching with `--hidden` when the app is already running leaves its current window visibility unchanged.

Drag the grip on the left of a repository row to change its position. The row follows your cursor while neighboring rows slide into place. Hold near the top or bottom of the list to scroll; press **Escape** to cancel. Releasing the handle saves the new order for the next session.

In **Settings → Notifications**, enable or disable desktop notifications and choose whether to receive alerts for available updates, completed downloads, or both. Optionally limit alerts to a daily **From / Until** window in your computer's local time. Overnight windows work; equal times allow all day. Available-update alerts exclude repositories disabled for bulk updates and announce each release once per app session. Update All sends one completion alert after the whole operation succeeds; individual updates and reinstalls send their own. Empty batches and failed operations do not send completion alerts.

Outside the allowed hours, completion alerts are skipped. Available updates are announced when the window opens if they are still pending. This requires the desktop app to remain running in the tray; the operating system's notification permissions and Do Not Disturb settings also apply.

On Linux, use the notification's **Open** button to show the desktop window. Clicking a Windows notification also brings the window forward. Hidden or minimized windows are restored, and clicking again keeps the window open.

### Release channels

Each repository defaults to **Stable releases only**. In desktop repository details or the web details dialog, choose **Stable releases and pre-releases** to also follow previews. Checks select the most recently published eligible release across all API pages using GitHub's pre-release metadata and publication date, including tags without version numbers. Drafts are excluded. The version selector lists published releases in the chosen channel; an explicit tag overrides the channel and remains pinned even if unavailable. Select `latest` to follow the channel again.

Details show the channel, installed version and offered release, or explain when no release is available. Channel changes only change the offered release; use **Update** to download or install. Settings survive restarts and configuration export/import. Manual checks, background checks and notifications use the same selection.

```bash
ghd repo 0 set-channel stable
ghd repo 0 set-channel prerelease
ghd check 0
ghd repo 0 set-version nightly-tag
```

The web repository API accepts and returns `includePrereleases` (boolean, default `false`) and returns `hasRelease` and `releaseStatus`. After changing the channel or tag, run an update check to resolve the offered release.

### Download and installation behavior

New repositories start with an editable asset rule for the architecture and operating system of the machine running GitHub Downloader. For example, a Debian/Ubuntu x64 machine uses `*{x64,amd64,x86_64}*.deb`; Windows uses `.msi`, macOS `.dmg`, RPM-based Linux distributions `.rpm`, and other Linux distributions `.AppImage`. Architecture aliases let the rule follow common naming conventions. Existing saved rules and manual selections are preserved. If a release uses a different format or omits the architecture from its filenames, edit or clear the rule.

In **More → Download Settings → Asset rule**, enter an exact filename or a case-sensitive glob such as `*linux*x64*.AppImage` to follow filenames that change between releases. `*` matches any text (including no characters); `?` matches one character; `{x64,amd64}` matches either literal alternative. Other characters are literal. The details view previews matching filenames. A rule takes precedence over the saved manual selection and must match **exactly one** asset. No matches or multiple matches block that repository's download and report the reason; other repositories in a bulk update continue.

Clear the rule to restore the saved manual filename or choose another file. If that filename is missing from a later release, no replacement is selected automatically. Existing configurations migrate the selected index to its filename using the cached asset list; an invalid or unavailable old selection requires choosing a file again. Rules and manual filenames are included in configuration exports.

| Asset | Behavior |
| --- | --- |
| `.deb` | Installed with `apt-get` on Linux; administrator authorization is required |
| `.AppImage` | Stored in the app's data directory, made executable, and given a Linux application-menu entry |
| `.exe` / `.msi` | Launched as an installer; installation may require user interaction |
| Other files, such as `.zip` or `.tar.gz` | Copied to the configured download directory |

For recognized installer assets, **Save file anyway** keeps an additional copy in your chosen download directory. It does not disable installation.

On Linux, use **Uninstall** in repository details to remove an installed `.deb` or AppImage. Confirm the action; Debian removal requests administrator authorization and preserves package configuration. AppImage removal deletes its managed executable, icon, and application-menu entry. Saved download copies and application settings are kept. The repository remains tracked and is excluded from **Update All** until you select it again. Use **Update** or **Reinstall** to install it again.

New Debian installations record the package name automatically. For older installations, enter the installed package name shown by your package manager when prompted. Changing the selected release asset does not change the recorded uninstall target. The trash button only removes a repository from the list; it keeps its installed application.

Enable **Auto-extract** in repository details to extract archives instead of keeping the archive file. Supported formats include ZIP, TAR, TAR.GZ / TGZ, TAR.BZ2, TAR.XZ, 7z, and RAR. The extracted `owner-repository` folder is saved inside **Download Location**, with no separate extraction directory to configure. Matching files are overwritten on updates; unrelated files are preserved. Password-protected archives and archive links are not supported. Other assets keep their normal download or installation behavior.

Choose an asset compatible with the machine running GitHub Downloader. Updating through the web interface downloads and installs on the **server**, not on the computer running your browser.

## Command-line usage

The CLI executable is named `ghd`. Repository IDs and asset IDs are **zero-based list positions**. Run `list repos` or `list assets` to find them; repository IDs can change after removing or reordering repositories.

```bash
# Open the desktop window (or bring the running tray app forward)
ghd --gui

# Add a repository by URL, or by owner and name
ghd add https://github.com/owner/repository
ghd add owner repository

# Inspect repositories, assets, and available versions
ghd list repos
ghd repo 0
ghd list assets 0
ghd list versions 0

# Select an asset and an existing download directory
ghd repo 0 set-asset 1
ghd repo 0 set-downloadpath "/path/to/downloads"

# Follow a filename pattern across releases; quote it to prevent shell expansion
ghd repo 0 set-asset-rule "*linux*x64*.AppImage"
ghd repo 0 clear-asset-rule

# Follow the latest release, or pin a tag returned by list versions
ghd repo 0 set-version latest
ghd repo 0 set-version v1.2.3

# Check for and install updates
ghd check
ghd check 0
ghd update 0
ghd update --all

# Download and install again, even if already up to date
ghd reinstall 0
ghd reinstall --all

# Stop tracking a repository
ghd remove 0
```

Replace the example repository, paths, IDs, and tag with your own values. `set-downloadpath` requires a directory that already exists. Use `ghd --help` for the command reference or `ghd --version` for the installed version. `ghd --gui` requires the desktop application to be installed; it checks beside `ghd`, the platform's installation directory, and directories on `PATH`.

## Local files

Choose **Install local file…** on the desktop to install a file already on your computer. Local `.deb` and `.AppImage` files use the same Linux package installation and application-menu integration as repository assets; `.exe` and `.msi` files use the existing executable installer handler. Supported archives can be extracted into a folder you choose. Windows executables require an operating system configured to run them.

The original file is kept. Results appear in **History** as **Local: filename**, without adding a tracked repository or contacting GitHub. Local files do not receive automatic update checks.

Successful local installations also appear in the separate **Local apps** tab and survive restarts. **Uninstall** removes Debian packages or AppImages through the same handlers as tracked repositories. For archives, it removes only newly created app files whose contents have not changed; pre-existing files, your edits, and the original archive are kept. Use a separate extraction folder for each app; another archive installed into the same folder updates that app's entry. Windows executable installers open the system uninstaller; remove their list entry after uninstalling there. Failed removals keep the app in the list.

The list and uninstall details are saved separately in `local-apps.json`, including extraction locations and file hashes. Clearing history or importing repository settings does not remove this list. CLI local installs appear here too.

```bash
ghd install-local "/path/to/package.deb"
ghd install-local "/path/to/application.AppImage"
ghd install-local "/path/to/archive.zip" --extract-to "/path/to/destination"
```

## Operation history

Open **History** from the desktop header or the web **History** tab to review downloads, installations, reinstalls, and uninstalls. Filter by repository, date range, and result; changes apply immediately. **Show download and installation steps** reveals the individual phases; batch entries summarize successes and failures. Skipped entries and batches with no actions are hidden by default. Enable **Show skipped** to include them. Times are shown in your local time zone. A date selected under **Through** includes that entire day.

History is saved separately in `history.json` inside the application data directory. It survives restarts and stays available after a repository is removed or a repository configuration is imported. Operations interrupted by a process exit are marked **Interrupted** when history is next read, rather than reported as successful. Deleting completed history requires confirmation; running entries are kept. History is not part of configuration exports. Technical logs remain separate.

A completed download does not confirm an installation. Files saved without installation use `lastDownloadTag`; `currentInstallTag` changes only after a successful installer or archive extraction. Failed repositories do not stop the remaining repositories in a batch. The web download endpoint saves files without running installers or extracting archives.

```bash
ghd history
ghd history --repo owner/repository --status Failed --json
ghd history --from 2026-10-01 --to 2026-11-01 --steps --json
ghd history --include-skipped --json
ghd history --action Uninstall --offset 0 --limit 100 --json
ghd history clear --yes
```

JSON output contains `entries`, `total`, `offset`, and `limit`. Each entry includes its ID, optional parent and batch IDs, repository name and opaque key, UTC start/end times, action, source/target versions, status, summary, and batch counts. `--batch <id>` selects one batch. `--include-skipped` includes skipped entries; filtering with `--status Skipped` also reveals them. Results are `Running`, `Succeeded`, `Failed`, `PartialFailure`, `Skipped`, or `Interrupted`. Actions are `Update`, `Download`, `Save`, `Install`, `Extract`, `Reinstall`, or `Uninstall`. `--to` is exclusive; timestamps without a timezone are interpreted as UTC. The page size is 1–1000 (default 200). History commands do not initialize repositories, contact GitHub, or clear the download cache. Invalid arguments return exit code 2; history access errors return 1.

The same data is available through `GET /api/history`, with `repository`, `from`, `to`, `status`, `action`, `batchId`, `offset`, `limit`, `includeSteps`, and `includeSkipped` query parameters. `DELETE /api/history?confirm=true` deliberately clears completed entries. Stored history uses fixed result messages and does not include request URLs, credentials, raw exceptions, or installer output.

## Web interface and API

The web UI provides **Repositories**, **Updates**, **Configuration**, and **Settings** tabs. Add repositories, adjust their assets and download settings, review changelogs, and follow operations in the activity panel.

Repository details include the asset rule and a live match preview. The repository API exposes `assetName` (saved manual filename), `assetRule`, `assetRuleMatches`, `assetSelectionStatus`, and the resolved `downloadAssetIndex` (`-1` when unresolved). Update a rule with `PUT /api/repositories/{repoUrl}` using `assetRule`; an empty string clears it. An explicit `downloadAssetIndex` selects a manual file when no nonempty rule is supplied. `POST /api/repositories/{repoUrl}/asset-rule-preview` with `{ "assetRule": "*linux*.zip" }` previews matches without saving. URL-encode `repoUrl` in these paths.

The packaged Linux service uses port **2008**. When running from source with the `http` launch profile, the default is **5033**.

> The web UI and API currently have no authentication. The packaged service listens on all network interfaces (`0.0.0.0:2008`). Restrict access to trusted users, and add authentication and HTTPS through a reverse proxy before making it remotely accessible.

## Personal access tokens and configuration

Public repositories can be accessed without a token. For private repositories or authenticated API access, set a GitHub personal access token using:

- **Desktop:** **Manage Personal Access Token** on the home screen.
- **Web:** **Settings → GitHub Personal Access Token**.
- **CLI:** `ghd pat set YOUR_TOKEN`; remove it with `ghd pat remove`.

Token storage is handled by `SecretsLib`, separately from the repository configuration. On Linux, the process needs access to the user's keyring session; this also matters when running the web service.

Use **Settings** in the desktop app or **Configuration** in the web UI to import or export repositories. Importing replaces the tracked repository configuration; export your current configuration first if you want to keep it. Tokens and desktop preferences are not included in that export.

## Troubleshooting

- **The desktop app is running but no window is visible:** launch it again without `--hidden` or open it from the tray icon. Autostart uses `--hidden`, and closing the window hides it rather than quitting.
- **A repository has no downloadable assets:** check that it publishes GitHub Releases with attached files, and select a release that contains an asset for your platform.
- **GitHub requests fail:** check connectivity, the repository URL, token access, and the application logs. A token can help with unauthenticated rate limits.
- **Debian installation fails:** confirm that the host has `apt-get` and working administrator authorization. The desktop app uses `pkexec`; the CLI and web service use `sudo`.
- **The web service fails to start:** inspect `systemctl --user status github-downloader-web.service` and `journalctl --user -u github-downloader-web.service`, including keyring/session errors.

Application action logs use LoggerLib for operation starts, outcomes, cancellations and failures. They omit local paths, repository identifiers, credentials, notification contents and raw installer output. Handled failures record exception types or HTTP/process exit codes, and debug and first-chance exception logging are disabled by default. Routine saves and property refreshes use debug level, while normal logs summarize each action. LoggerLib retains its own file header and crash handling.

## Contributing

Bug reports and feature requests are welcome in [Issues](https://github.com/Tobias2888/Github-Downloader/issues). For bugs, include your operating system and architecture, the GitHub Downloader version, the interface you used, the affected repository and asset, and relevant logs with credentials removed.

For code changes, open a pull request explaining the behavior you changed and how you checked it. Build the affected projects before submitting; changes to the shared library can affect all three interfaces.

Run the archive extraction and settings regression tests with:

```bash
dotnet test Github-Downloader.Tests/Github-Downloader.Tests.csproj
```

## License

Licensed under the [GNU General Public License v3.0](LICENSE). Third-party components retain their own licenses; vendored web-library notices are included in `Github-Downloader-Web/wwwroot/vendor/`.
