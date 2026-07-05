# Deployment (Docker Compose)

## Prerequisites

- Docker + Docker Compose on the target Linux machine
- The ESP devices and the machine share a network; device IPs are pinned
  via DHCP reservation in the router
- Outbound TCP 6053 from the machine to the ESP devices must be allowed

## First deployment

```sh
git clone <repo> homepeter && cd homepeter

# Config lives OUTSIDE the container in ./data (survives rebuilds)
mkdir -p data
cp config.json cycles.json data/    # then adjust data/config.json to the environment

docker compose up -d --build
```

Dashboard: `http://<host>:5010`

## Configuration changes

Use the gear icon in the dashboard: **Save & restart** stops the app after
writing the config; `restart: unless-stopped` brings it back up with the
new settings. No shell access needed.

Editing `data/config.json` / `data/cycles.json` by hand works too:
`docker compose restart` afterwards.

## Updating the app

```sh
git pull
docker compose up -d --build
```

## Logs

```sh
docker compose logs -f homepeter
```

## Notes

- Sensor history is in-memory and starts empty after every restart.
- The cycle scheduler re-enforces states every 30s (lights) / on mismatch
  and every 5 min unconditionally (AC Infinity fans), so devices recover
  automatically after outages.
