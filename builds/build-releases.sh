read -p "Enter new version number: " NEW_VERSION

dotnet publish ../Github-Downloader/Github-Downloader.csproj \
    -c Release \
    -r linux-x64 \
    --self-contained true \
    --output ./github-downloader-linux-x64/opt/Github-Downloader \
    /p:PublishSingleFile=true \
    /p:PublishReadyToRun=true

dotnet publish ../Github-Downloader/Github-Downloader.csproj \
    -c Release \
    -r linux-x64 \
    --self-contained true \
    --output ./github-downloader-linux-x64-appimage/usr/bin \
    /p:PublishSingleFile=true \
    /p:PublishReadyToRun=true

dotnet publish ../Github-Downloader/Github-Downloader.csproj \
    -c Release \
    -r linux-arm64 \
    --self-contained true \
    --output ./github-downloader-linux-arm64/opt/Github-Downloader \
    /p:PublishSingleFile=true \
    /p:PublishReadyToRun=true

dotnet publish ../Github-Downloader/Github-Downloader.csproj \
    -c Release \
    -r osx-x64 \
    --self-contained true \
    --output ./github-downloader-osx-x64 \
    /p:PublishSingleFile=true \
    /p:PublishReadyToRun=true

dotnet publish ../Github-Downloader/Github-Downloader.csproj \
    -c Release \
    -r osx-arm64 \
    --self-contained true \
    --output ./github-downloader-osx-arm64 \
    /p:PublishSingleFile=true \
    /p:PublishReadyToRun=true

dotnet publish ../Github-Downloader/Github-Downloader.csproj \
    -c Release \
    -r win-x64 \
    --self-contained true \
    --output ./github-downloader-win-x64 \
    /p:PublishSingleFile=true \
    /p:PublishReadyToRun=true

dotnet publish ../Github-Downloader/Github-Downloader.csproj \
    -c Release \
    -r win-arm64 \
    --self-contained true \
    --output ./github-downloader-win-arm64 \
    /p:PublishSingleFile=true \
    /p:PublishReadyToRun=true


CONTROL_FILE_X64="./github-downloader-linux-x64/DEBIAN/control"
CONTROL_FILE_ARM64="./github-downloader-linux-arm64/DEBIAN/control"

if [ ! -f "$CONTROL_FILE_X64" ]; then
    echo "Control file not found at $CONTROL_FILE_X64"
    exit 1
fi
if [ ! -f "$CONTROL_FILE_ARM64" ]; then
    echo "Control file not found at $CONTROL_FILE_ARM64"
    exit 1
fi

if [ -z "$NEW_VERSION" ]; then
    echo "No version entered. Exiting."
    exit 1
fi

sed -i "s/^Version: .*/Version: $NEW_VERSION/" "$CONTROL_FILE_X64"
sed -i "s/^Version: .*/Version: $NEW_VERSION/" "$CONTROL_FILE_ARM64"


rm -rf release-assets/*

dpkg-deb --build github-downloader-linux-x64 ./release-assets
dpkg-deb --build github-downloader-linux-arm64 ./release-assets

ARCH=x86_64 ./AppImage-appimagetool.AppImage ./github-downloader-linux-x64-appimage
mv ./Github_Downloader-x86_64.AppImage release-assets/Github_Downloader-x86_64.AppImage

zip -r release-assets/github-downloader-win-x64.zip github-downloader-win-x64
zip -r release-assets/github-downloader-osx-x64.zip github-downloader-osx-x64
zip -r release-assets/github-downloader-win-arm64.zip github-downloader-win-arm64
zip -r release-assets/github-downloader-osx-arm64.zip github-downloader-osx-arm64

printf "\nBuild Finished. Press Enter to exit"
read _
