# TLS Origin Certificates

Place your Cloudflare Origin Certificate files in this directory on the production server before starting `docker-compose.prod.yml`:

- `origin.pem` — Cloudflare Origin Certificate (covering `$DOMAIN` and `*.$DOMAIN`)
- `origin.key` — Matching private key (`chmod 600 certs/origin.key`)

This directory is mounted read-only into Caddy at `/certs`. Private key and certificate files are gitignored and must never be committed.
