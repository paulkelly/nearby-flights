# Multi-stage build for the web app. Build context is web/ (see compose.yaml).
# Stage 1 builds the static site with Node; stage 2 serves it with nginx. Node, node_modules and the source
# code never reach the final image, only the compiled files in dist/.

# ---- Build ----
FROM node:24-alpine AS build
WORKDIR /app

# Dependencies first, for layer caching: this slow step only re-runs when package*.json change.
# `npm ci` installs exactly what package-lock.json records (and fails if it's out of sync with package.json),
# so the image gets the same versions you tested with.
COPY package.json package-lock.json ./
RUN npm ci

COPY . .
# Type-checks (tsc -b) and bundles into dist/.
RUN npm run build

# ---- Runtime ----
# The nginx team's unprivileged variant: identical to the official image but runs as a non-root user,
# listening on 8080 (ports below 1024 need root). Pinned to a minor version so rebuilds get security patches
# but no surprise upgrades.
FROM nginxinc/nginx-unprivileged:1.30-alpine AS final

# Where nginx forwards /api requests. Compose's service name by default; overridable per environment.
ENV API_URL=http://api:8080
# Tells the image's startup script to read the container's DNS server from /etc/resolv.conf into
# $NGINX_LOCAL_RESOLVERS, so nginx.conf needn't hard-code Docker's DNS address.
ENV NGINX_ENTRYPOINT_LOCAL_RESOLVERS=1

# Files in /etc/nginx/templates get their ${VARIABLES} filled in from the environment at container start,
# and are written to /etc/nginx/conf.d/. One image, configured per environment, no rebuild.
COPY nginx.conf.template /etc/nginx/templates/default.conf.template
COPY --from=build /app/dist /usr/share/nginx/html

EXPOSE 8080
