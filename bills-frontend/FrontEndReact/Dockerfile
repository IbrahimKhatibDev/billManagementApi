# ---------------------------------------------------------------------------
# Build stage — produce the static bundle.
# ---------------------------------------------------------------------------
FROM node:22-alpine AS build
WORKDIR /src

# Lockfile first, so this layer is only rebuilt when dependencies actually
# change rather than on every source edit. `npm ci` rather than `npm install`:
# it installs exactly what package-lock.json pins and fails if the two disagree.
COPY package.json package-lock.json ./
RUN npm ci

# Vite inlines VITE_* variables into the bundle at build time — there is no
# runtime configuration to change afterwards, so the API URL has to be decided
# here. The default matches the API's published host port, because this is a
# static SPA: the request is made by the browser, not by this container, so the
# compose-network name http://api:8080 would not resolve.
ARG VITE_API_BASE_URL=http://localhost:5131/restapi/BillDtos
ENV VITE_API_BASE_URL=$VITE_API_BASE_URL

COPY . .
RUN npm run build

# ---------------------------------------------------------------------------
# Serve stage — nginx over the built assets. Nothing from the build stage but
# dist/ survives, so node and node_modules are not in the shipped image.
# ---------------------------------------------------------------------------
FROM nginx:1.27-alpine AS final

COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /src/dist /usr/share/nginx/html

EXPOSE 80
