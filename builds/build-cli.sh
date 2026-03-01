read -p "Enter new version number: " NEW_VERSION

dotnet restore ../Github-Downloader-cli/Github-Downloader-cli.csproj -r linux-x64
dotnet publish ../Github-Downloader-cli/Github-Downloader-cli.csproj \
    --no-restore \
    -c Release \
    -r linux-x64 \
    --self-contained true \
    --output ./github-downloader-cli-linux-x64/usr/local/bin \
    /p:PublishSingleFile=true \
    /p:PublishReadyToRun=true \
    /p:DebugType=None \
    /p:DebugSymbols=false


CONTROL_FILE_X64_CLI="./github-downloader-cli-linux-x64/DEBIAN/control"

if [ ! -f "$CONTROL_FILE_X64_CLI" ]; then
    echo "Control file not found at $CONTROL_FILE_X64_CLI"
    exit 1
fi

sed -i "s/^Version: .*/Version: $NEW_VERSION/" "$CONTROL_FILE_X64_CLI"

dpkg-deb --build github-downloader-cli-linux-x64
