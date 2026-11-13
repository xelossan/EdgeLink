# winget source

EdgeLink can be installed and updated with winget from a small self-hosted source. Nothing but
manifests is hosted there; the executables are downloaded from the GitHub releases.

How a release reaches winget:

1. A `v*` tag makes the release workflow run `New-Manifest.ps1` and attach
   `xelossan.EdgeLink.yaml` to the GitHub release.
2. On the server, `rewinged-sync` (hourly systemd timer) copies manifests of new releases into
   `/var/lib/rewinged/manifests`. It only accepts manifests whose installer URL points at this
   repository's releases.
3. [rewinged](https://github.com/jantari/rewinged) serves that directory as a winget REST source
   on `127.0.0.1:8090`; nginx publishes it over HTTPS under `/winget/`.

## Server setup

Any Linux machine with nginx and a valid HTTPS certificate will do.

```sh
sudo useradd --system --home-dir /var/lib/rewinged --shell /usr/sbin/nologin rewinged
sudo install -d -o rewinged -g rewinged /var/lib/rewinged/manifests
sudo install -m 755 rewinged /usr/local/bin/rewinged            # built from the rewinged sources
sudo install -m 755 rewinged-sync /usr/local/bin/rewinged-sync
sudo install -m 644 rewinged.service rewinged-sync.service rewinged-sync.timer /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable --now rewinged.service rewinged-sync.timer
```

Include `nginx-winget.conf` in the HTTPS server block and reload nginx.
`sudo systemctl start rewinged-sync` fetches manifests immediately instead of waiting for the timer.

## Client

In an elevated terminal, once:

```powershell
winget source add -n edgelink -a https://<host>/winget/api -t Microsoft.Rest
```

Then:

```powershell
winget install xelossan.EdgeLink -s edgelink
winget upgrade xelossan.EdgeLink
```

The package is a portable executable that needs the .NET 10 Desktop Runtime
(`winget install Microsoft.DotNet.DesktopRuntime.10`). Close EdgeLink before upgrading it.
