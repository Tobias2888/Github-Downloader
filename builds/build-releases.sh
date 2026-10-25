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

VERSION=$(sed -n 's/^Version: //p' "$CONTROL_FILE_X64")
echo "Current version: $VERSION"

read -p "Enter new version number: " NEW_VERSION

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

sed -i "s/^Version: .*/Version: $NEW_VERSION/" "$CONTROL_FILE_X64"
sed -i "s/^Version: .*/Version: $NEW_VERSION/" "$CONTROL_FILE_ARM64"
sed -i "s/^Version: .*/Version: $NEW_VERSION/" "$CONTROL_FILE_X64_CLI"
sed -i "s/^Version: .*/Version: $NEW_VERSION/" "$CONTROL_FILE_ARM64_CLI"
sed -i "s/^Version: .*/Version: $NEW_VERSION/" "$CONTROL_FILE_X64_WEB"
sed -i "s/^Version: .*/Version: $NEW_VERSION/" "$CONTROL_FILE_ARM64_WEB"
sed -i "s/^\(!define VERSION\s\+\)\"[^\"]\+\"/\1\"$NEW_VERSION\"/" "$NSI_FILE_X64"
sed -i "s/^\(!define VERSION\s\+\)\"[^\"]\+\"/\1\"$NEW_VERSION\"/" "$NSI_FILE_ARM64"

dotnet restore ../Github-Downloader-cli/Github-Downloader-cli.csproj -r linux-x64
dotnet publish ../Github-Downloader-cli/Github-Downloader-cli.csproj \
    --no-restore \
    -c Release \
    -r linux-x64 \
    --self-contained true \
    --output "$CLI_LINUX_X64_PACKAGE_DIR/usr/local/bin" \
    /p:PublishSingleFile=true \
    /p:DebugType=None \
    /p:DebugSymbols=false

dotnet restore ../Github-Downloader-cli/Github-Downloader-cli.csproj -r linux-arm64
dotnet publish ../Github-Downloader-cli/Github-Downloader-cli.csproj \
    --no-restore \
    -c Release \
    -r linux-arm64 \
    --self-contained true \
    --output "$CLI_LINUX_ARM64_PACKAGE_DIR/usr/local/bin" \
    /p:PublishSingleFile=true \
    /p:DebugType=None \
    /p:DebugSymbols=false

dotnet restore ../Github-Downloader-cli/Github-Downloader-cli.csproj -r linux-x64
dotnet publish ../Github-Downloader-cli/Github-Downloader-cli.csproj \
    --no-restore \
    -c Release \
    -r linux-x64 \
    --self-contained true \
    --output "$CLI_ARCH_X64_PACKAGE_DIR/usr/bin" \
    /p:PublishSingleFile=true \
    /p:DebugType=None \
    /p:DebugSymbols=false

dotnet restore ../Github-Downloader-cli/Github-Downloader-cli.csproj -r linux-arm64
dotnet publish ../Github-Downloader-cli/Github-Downloader-cli.csproj \
    --no-restore \
    -c Release \
    -r linux-arm64 \
    --self-contained true \
    --output "$CLI_ARCH_ARM64_PACKAGE_DIR/usr/bin" \
    /p:PublishSingleFile=true \
    /p:DebugType=None \
    /p:DebugSymbols=false

dotnet restore ../Github-Downloader/Github-Downloader.csproj -r linux-x64
dotnet publish ../Github-Downloader/Github-Downloader.csproj \
    --no-restore \
    -c Release \
    -r linux-x64 \
    --self-contained true \
    --output "$ARCH_PACKAGE_DIR/opt/Github-Downloader" \
    /p:PublishSingleFile=true

dotnet restore ../Github-Downloader/Github-Downloader.csproj -r linux-arm64
dotnet publish ../Github-Downloader/Github-Downloader.csproj \
    --no-restore \
    -c Release \
    -r linux-arm64 \
    --self-contained true \
    --output "$ARCH_ARM64_PACKAGE_DIR/opt/Github-Downloader" \
    /p:PublishSingleFile=true

dotnet restore ../Github-Downloader/Github-Downloader.csproj -r linux-x64
dotnet publish ../Github-Downloader/Github-Downloader.csproj \
    --no-restore \
    -c Release \
    -r linux-x64 \
    --self-contained true \
    --output "$LINUX_X64_PACKAGE_DIR/opt/Github-Downloader" \
    /p:PublishSingleFile=true \

dotnet restore ../Github-Downloader/Github-Downloader.csproj -r linux-x64
dotnet publish ../Github-Downloader/Github-Downloader.csproj \
    --no-restore \
    -c Release \
    -r linux-x64 \
    --self-contained true \
    --output "$APPIMAGE_PACKAGE_DIR/usr/bin" \
    /p:PublishSingleFile=true \

dotnet restore ../Github-Downloader/Github-Downloader.csproj -r linux-arm64
dotnet publish ../Github-Downloader/Github-Downloader.csproj \
    --no-restore \
    -c Release \
    -r linux-arm64 \
    --self-contained true \
    --output "$LINUX_ARM64_PACKAGE_DIR/opt/Github-Downloader" \
    /p:PublishSingleFile=true \

dotnet restore ../Github-Downloader/Github-Downloader.csproj -r osx-x64
dotnet publish ../Github-Downloader/Github-Downloader.csproj \
    --no-restore \
    -c Release \
    -r osx-x64 \
    --self-contained true \
    --output "$OSX_X64_PUBLISH_DIR" \
    /p:PublishSingleFile=true \

dotnet restore ../Github-Downloader/Github-Downloader.csproj -r osx-arm64
dotnet publish ../Github-Downloader/Github-Downloader.csproj \
    --no-restore \
    -c Release \
    -r osx-arm64 \
    --self-contained true \
    --output "$OSX_ARM64_PUBLISH_DIR" \
    /p:PublishSingleFile=true \

dotnet restore ../Github-Downloader/Github-Downloader.csproj -r win-x64
dotnet publish ../Github-Downloader/Github-Downloader.csproj \
    --no-restore \
    -c Release \
    -r win-x64 \
    --self-contained true \
    --output "$WIN_X64_PUBLISH_DIR" \
    /p:PublishSingleFile=true \

dotnet restore ../Github-Downloader/Github-Downloader.csproj -r win-arm64
dotnet publish ../Github-Downloader/Github-Downloader.csproj \
    --no-restore \
    -c Release \
    -r win-arm64 \
    --self-contained true \
    --output "$WIN_ARM64_PUBLISH_DIR" \
    /p:PublishSingleFile=true \

dotnet restore ../Github-Downloader-Web/Github-Downloader-Web.csproj -r linux-x64
dotnet publish ../Github-Downloader-Web/Github-Downloader-Web.csproj \
    --no-restore \
    -c Release \
    -r linux-x64 \
    --self-contained true \
    --output "$WEB_LINUX_X64_PACKAGE_DIR/opt/Github-Downloader-Web" \
    /p:PublishSingleFile=true \
    /p:DebugType=None \
    /p:DebugSymbols=false

dotnet restore ../Github-Downloader-Web/Github-Downloader-Web.csproj -r linux-arm64
dotnet publish ../Github-Downloader-Web/Github-Downloader-Web.csproj \
    --no-restore \
    -c Release \
    -r linux-arm64 \
    --self-contained true \
    --output "$WEB_LINUX_ARM64_PACKAGE_DIR/opt/Github-Downloader-Web" \
    /p:PublishSingleFile=true \
    /p:DebugType=None \
    /p:DebugSymbols=false


rm -rf release-assets/*

dpkg-deb --build "$LINUX_X64_PACKAGE_DIR" ./release-assets
dpkg-deb --build "$LINUX_ARM64_PACKAGE_DIR" ./release-assets
dpkg-deb --build "$CLI_LINUX_X64_PACKAGE_DIR" ./release-assets
dpkg-deb --build "$CLI_LINUX_ARM64_PACKAGE_DIR" ./release-assets
dpkg-deb --build "$WEB_LINUX_X64_PACKAGE_DIR" ./release-assets
dpkg-deb --build "$WEB_LINUX_ARM64_PACKAGE_DIR" ./release-assets

ARCH_PACKAGE_VERSION="$NEW_VERSION-1"
ARCH_BUILD_DATE=$(date +%s)
build_arch_package() {
    local package_dir="$1"
    local package_arch="$2"
    local package_name="$3"
    shift 3
    local package_entries=("$@")
    local installed_size
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

ARCH=x86_64 ./AppImage-appimagetool.AppImage "$APPIMAGE_PACKAGE_DIR"
mv ./Github_Downloader-x86_64.AppImage release-assets/Github_Downloader-x86_64.AppImage

makensis win-x64.nsi
makensis win-arm64.nsi

zip -r release-assets/github-downloader-osx-x64.zip "$OSX_X64_PUBLISH_DIR"
zip -r release-assets/github-downloader-osx-arm64.zip "$OSX_ARM64_PUBLISH_DIR"

printf "\nBuild Finished. Press Enter to exit"
read _
