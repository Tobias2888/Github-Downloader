rm -rf release-assets/*

dpkg-deb --build github-downloader-linux-x64 ./release-assets
dpkg-deb --build github-downloader-linux-arm64 ./release-assets

zip -r release-assets/github-downloader-win-x64.zip github-downloader-win-x64
zip -r release-assets/github-downloader-osx-x64.zip github-downloader-osx-x64
