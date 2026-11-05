#!/usr/bin/env bash

show_title() {
    if [[ -t 1 && "${TERM:-dumb}" != "dumb" && ! -v NO_COLOR ]]; then
        printf '\n\033[36m──\033[0m \033[1m%s\033[0m \033[36m──\033[0m\n' "$1"
    else
        printf '\n── %s ──\n' "$1"
    fi
}

LINUX_X64_PACKAGE_DIR="./github-downloader-linux-x64"
LINUX_ARM64_PACKAGE_DIR="./github-downloader-linux-arm64"
CLI_LINUX_X64_PACKAGE_DIR="./github-downloader-cli-linux-x64"
CLI_LINUX_ARM64_PACKAGE_DIR="./github-downloader-cli-linux-arm64"
WEB_LINUX_X64_PACKAGE_DIR="./github-downloader-web-linux-x64"
WEB_LINUX_ARM64_PACKAGE_DIR="./github-downloader-web-linux-arm64"
APPIMAGE_PACKAGE_DIR="./github-downloader-linux-x64-appimage"
OSX_X64_PUBLISH_DIR="./github-downloader-osx-x64"
OSX_ARM64_PUBLISH_DIR="./github-downloader-osx-arm64"
WIN_X64_PUBLISH_DIR="./win-x64"
WIN_ARM64_PUBLISH_DIR="./win-arm64"
ARCH_PACKAGE_DIR="./github-downloader-linux-x64-arch"
ARCH_ARM64_PACKAGE_DIR="./github-downloader-linux-arm64-arch"
CLI_ARCH_X64_PACKAGE_DIR="./github-downloader-cli-linux-x64-arch"
CLI_ARCH_ARM64_PACKAGE_DIR="./github-downloader-cli-linux-arm64-arch"

CONTROL_FILE_X64="$LINUX_X64_PACKAGE_DIR/DEBIAN/control"
CONTROL_FILE_ARM64="$LINUX_ARM64_PACKAGE_DIR/DEBIAN/control"
CONTROL_FILE_X64_CLI="$CLI_LINUX_X64_PACKAGE_DIR/DEBIAN/control"
CONTROL_FILE_ARM64_CLI="$CLI_LINUX_ARM64_PACKAGE_DIR/DEBIAN/control"
CONTROL_FILE_X64_WEB="$WEB_LINUX_X64_PACKAGE_DIR/DEBIAN/control"
CONTROL_FILE_ARM64_WEB="$WEB_LINUX_ARM64_PACKAGE_DIR/DEBIAN/control"
NSI_FILE_X64="./win-x64.nsi"
NSI_FILE_ARM64="./win-arm64.nsi"
ARCH_PKGINFO_FILE="$ARCH_PACKAGE_DIR/.PKGINFO"
ARCH_ARM64_PKGINFO_FILE="$ARCH_ARM64_PACKAGE_DIR/.PKGINFO"
CLI_ARCH_X64_PKGINFO_FILE="$CLI_ARCH_X64_PACKAGE_DIR/.PKGINFO"
CLI_ARCH_ARM64_PKGINFO_FILE="$CLI_ARCH_ARM64_PACKAGE_DIR/.PKGINFO"

show_title "Preparing release version"
VERSION_PROPS="../Directory.Build.props"
if [ ! -f "$VERSION_PROPS" ]; then
    echo "Version properties not found at $VERSION_PROPS"
    exit 1
fi
VERSION=$(sed -n 's/^[[:space:]]*<Version>\([^<]*\)<\/Version>[[:space:]]*$/\1/p' "$VERSION_PROPS")
if [ -z "$VERSION" ]; then
    echo "Could not read Version from $VERSION_PROPS"
    exit 1
fi
echo "Current version: $VERSION"

read -p "Enter new version number: " NEW_VERSION

show_title "Checking package metadata"
if [ ! -f "$CONTROL_FILE_X64" ]; then
    echo "Control file not found at $CONTROL_FILE_X64"
    exit 1
fi
if [ ! -f "$CONTROL_FILE_ARM64" ]; then
    echo "Control file not found at $CONTROL_FILE_ARM64"
    exit 1
fi
if [ ! -f "$CONTROL_FILE_X64_CLI" ]; then
    echo "Control file not found at $CONTROL_FILE_X64_CLI"
    exit 1
fi
if [ ! -f "$CONTROL_FILE_ARM64_CLI" ]; then
    echo "Control file not found at $CONTROL_FILE_ARM64_CLI"
    exit 1
fi
if [ ! -f "$CONTROL_FILE_ARM64_WEB" ]; then
    echo "Control file not found at $CONTROL_FILE_ARM64_WEB"
    exit 1
fi
if [ ! -f "$CONTROL_FILE_X64_WEB" ]; then
    echo "Control file not found at $CONTROL_FILE_X64_WEB"
    exit 1
fi
if [ ! -f "$NSI_FILE_X64" ]; then
    echo "Control file not found at $NSI_FILE_X64"
    exit 1
fi
if [ ! -f "$NSI_FILE_ARM64" ]; then
    echo "Control file not found at $NSI_FILE_ARM64"
    exit 1
fi
if [ ! -f "$ARCH_PKGINFO_FILE" ]; then
    echo "Arch package metadata not found at $ARCH_PKGINFO_FILE"
    exit 1
fi
if [ ! -f "$ARCH_ARM64_PKGINFO_FILE" ]; then
    echo "Arch package metadata not found at $ARCH_ARM64_PKGINFO_FILE"
    exit 1
fi

for pkginfo_file in "$CLI_ARCH_X64_PKGINFO_FILE" "$CLI_ARCH_ARM64_PKGINFO_FILE"; do
    if [ ! -f "$pkginfo_file" ]; then
        echo "Arch package metadata not found at $pkginfo_file"
        exit 1
    fi
done

if [ -z "$NEW_VERSION" ]; then
    echo "No version entered. Exiting."
    exit 1
fi

if [[ ! "$NEW_VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
    echo "Enter a version in major.minor.patch format (for example, 3.5.2)."
    exit 1
fi

show_title "Updating application and package versions to $NEW_VERSION"
sed -i "s|<Version>[^<]*</Version>|<Version>$NEW_VERSION</Version>|" "$VERSION_PROPS"
sed -i "s/^Version: .*/Version: $NEW_VERSION/" "$CONTROL_FILE_X64"
sed -i "s/^Version: .*/Version: $NEW_VERSION/" "$CONTROL_FILE_ARM64"
sed -i "s/^Version: .*/Version: $NEW_VERSION/" "$CONTROL_FILE_X64_CLI"
sed -i "s/^Version: .*/Version: $NEW_VERSION/" "$CONTROL_FILE_ARM64_CLI"
sed -i "s/^Version: .*/Version: $NEW_VERSION/" "$CONTROL_FILE_X64_WEB"
sed -i "s/^Version: .*/Version: $NEW_VERSION/" "$CONTROL_FILE_ARM64_WEB"
sed -i "s/^\(!define VERSION\s\+\)\"[^\"]\+\"/\1\"$NEW_VERSION\"/" "$NSI_FILE_X64"
sed -i "s/^\(!define VERSION\s\+\)\"[^\"]\+\"/\1\"$NEW_VERSION\"/" "$NSI_FILE_ARM64"

# Publish each application/runtime pair once, then reuse its complete output.
PUBLISH_ROOT=$(mktemp -d) || exit 1
trap 'rm -rf "$PUBLISH_ROOT"' EXIT

publish_and_copy() {
    local project="$1"
    local runtime="$2"
    shift 2
    local project_file="../$project/$project.csproj"
    local publish_dir="$PUBLISH_ROOT/$project/$runtime"
    local destination
    local publish_options=()

    if [ "$project" != "GithubDownloader.Desktop" ]; then
        publish_options+=(/p:DebugType=None /p:DebugSymbols=false)
    fi

    show_title "Restoring $project ($runtime)"
    dotnet restore "$project_file" -r "$runtime" || return 1
    show_title "Publishing $project ($runtime)"
    dotnet publish "$project_file" \
        --no-restore \
        -c Release \
        -r "$runtime" \
        --self-contained true \
        --output "$publish_dir" \
        /p:PublishSingleFile=true \
        "${publish_options[@]}" || return 1

    for destination in "$@"; do
        show_title "Copying $project ($runtime) to $destination"
        mkdir -p "$destination" || return 1
        cp -a "$publish_dir/." "$destination/" || return 1
    done
}

publish_and_copy GithubDownloader.Cli linux-x64 \
    "$CLI_LINUX_X64_PACKAGE_DIR/usr/local/bin" \
    "$CLI_ARCH_X64_PACKAGE_DIR/usr/bin" || exit 1
publish_and_copy GithubDownloader.Cli linux-arm64 \
    "$CLI_LINUX_ARM64_PACKAGE_DIR/usr/local/bin" \
    "$CLI_ARCH_ARM64_PACKAGE_DIR/usr/bin" || exit 1

publish_and_copy GithubDownloader.Desktop linux-x64 \
    "$LINUX_X64_PACKAGE_DIR/opt/Github-Downloader" \
    "$ARCH_PACKAGE_DIR/opt/Github-Downloader" \
    "$APPIMAGE_PACKAGE_DIR/usr/bin" || exit 1
publish_and_copy GithubDownloader.Desktop linux-arm64 \
    "$LINUX_ARM64_PACKAGE_DIR/opt/Github-Downloader" \
    "$ARCH_ARM64_PACKAGE_DIR/opt/Github-Downloader" || exit 1
publish_and_copy GithubDownloader.Desktop osx-x64 "$OSX_X64_PUBLISH_DIR" || exit 1
publish_and_copy GithubDownloader.Desktop osx-arm64 "$OSX_ARM64_PUBLISH_DIR" || exit 1
publish_and_copy GithubDownloader.Desktop win-x64 "$WIN_X64_PUBLISH_DIR" || exit 1
publish_and_copy GithubDownloader.Desktop win-arm64 "$WIN_ARM64_PUBLISH_DIR" || exit 1

publish_and_copy GithubDownloader.Web linux-x64 \
    "$WEB_LINUX_X64_PACKAGE_DIR/opt/Github-Downloader-Web" || exit 1
publish_and_copy GithubDownloader.Web linux-arm64 \
    "$WEB_LINUX_ARM64_PACKAGE_DIR/opt/Github-Downloader-Web" || exit 1

show_title "Clearing previous release assets"
rm -rf release-assets/*

show_title "Building Debian package: $LINUX_X64_PACKAGE_DIR"
dpkg-deb --root-owner-group --build "$LINUX_X64_PACKAGE_DIR" ./release-assets
show_title "Building Debian package: $LINUX_ARM64_PACKAGE_DIR"
dpkg-deb --root-owner-group --build "$LINUX_ARM64_PACKAGE_DIR" ./release-assets
show_title "Building Debian package: $CLI_LINUX_X64_PACKAGE_DIR"
dpkg-deb --root-owner-group --build "$CLI_LINUX_X64_PACKAGE_DIR" ./release-assets
show_title "Building Debian package: $CLI_LINUX_ARM64_PACKAGE_DIR"
dpkg-deb --root-owner-group --build "$CLI_LINUX_ARM64_PACKAGE_DIR" ./release-assets
show_title "Building Debian package: $WEB_LINUX_X64_PACKAGE_DIR"
dpkg-deb --root-owner-group --build "$WEB_LINUX_X64_PACKAGE_DIR" ./release-assets
show_title "Building Debian package: $WEB_LINUX_ARM64_PACKAGE_DIR"
dpkg-deb --root-owner-group --build "$WEB_LINUX_ARM64_PACKAGE_DIR" ./release-assets

ARCH_PACKAGE_VERSION="$NEW_VERSION-1"
ARCH_BUILD_DATE=$(date +%s)
build_arch_package() {
    local package_dir="$1"
    local package_arch="$2"
    local package_name="$3"
    shift 3
    local package_entries=("$@")
    local installed_size
    show_title "Building Arch package: $package_name ($package_arch)"
    installed_size=$(du -sb "${package_entries[@]/#/$package_dir/}" | awk '{size += $1} END {print size}')
    sed -i "s/^pkgver = .*/pkgver = $ARCH_PACKAGE_VERSION/; s/^builddate = .*/builddate = $ARCH_BUILD_DATE/; s/^size = .*/size = $installed_size/; s/^arch = .*/arch = $package_arch/" "$package_dir/.PKGINFO" || return 1
    tar --format=gnu \
        --numeric-owner \
        --owner=0 --group=0 \
        -I 'zstd -19' \
        -cf "./release-assets/$package_name-$ARCH_PACKAGE_VERSION-$package_arch.pkg.tar.zst" \
        -C "$package_dir" \
        .PKGINFO "${package_entries[@]}"
}

build_arch_package "$ARCH_PACKAGE_DIR" x86_64 github-downloader etc opt usr || exit 1
build_arch_package "$ARCH_ARM64_PACKAGE_DIR" aarch64 github-downloader etc opt usr || exit 1
build_arch_package "$CLI_ARCH_X64_PACKAGE_DIR" x86_64 github-downloader-cli usr || exit 1
build_arch_package "$CLI_ARCH_ARM64_PACKAGE_DIR" aarch64 github-downloader-cli usr || exit 1

show_title "Building AppImage (linux-x64)"
ARCH=x86_64 ./AppImage-appimagetool.AppImage "$APPIMAGE_PACKAGE_DIR"
mv ./Github_Downloader-x86_64.AppImage release-assets/Github_Downloader-x86_64.AppImage

show_title "Building Windows installer (win-x64)"
makensis win-x64.nsi
show_title "Building Windows installer (win-arm64)"
makensis win-arm64.nsi

show_title "Creating macOS archive (osx-x64)"
zip -r release-assets/github-downloader-osx-x64.zip "$OSX_X64_PUBLISH_DIR"
show_title "Creating macOS archive (osx-arm64)"
zip -r release-assets/github-downloader-osx-arm64.zip "$OSX_ARM64_PUBLISH_DIR"

printf "\nBuild Finished. Press Enter to exit"
read _
