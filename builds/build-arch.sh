cd github-downloader-linux-amd64-arch
tar --format=gnu \
    --numeric-owner \
    --owner=0 --group=0 \
    -cf ../github-downloader-1.3.2-1-x86_64.pkg.tar \
.PKGINFO etc opt usr

cd ..
zstd -19 github-downloader-1.3.2-1-x86_64.pkg.tar

