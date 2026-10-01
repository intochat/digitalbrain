# Serves the shell's release web bundle as a container resource: compile once per source
# change (docker layer cache keys on the workspace content), serve from nginx for every
# AppHost session. The bundle is built without --dart-define so the kernel URL resolves at
# serve time from /config.json (see DigitalBrainHostEnv.loadServedConfig).
# Context is the pub workspace root (app/); shell needs its core and ui siblings.
# No public SDK image tracks current Flutter releases, so the build stage installs the
# pinned SDK from the official release archive; the layer caches until the pin moves.
FROM debian:bookworm-slim AS build
RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates curl git unzip xz-utils \
    && rm -rf /var/lib/apt/lists/*
ARG FLUTTER_VERSION=3.47.2
RUN curl -fsSL "https://storage.googleapis.com/flutter_infra_release/releases/stable/linux/flutter_linux_${FLUTTER_VERSION}-stable.tar.xz" \
        | tar -xJ -C /opt \
    && git config --global --add safe.directory /opt/flutter
ENV PATH="/opt/flutter/bin:${PATH}"
RUN flutter --version && flutter precache --web
WORKDIR /workspace
# Pubspecs first: a source edit then reuses the resolved-packages layer.
COPY pubspec.yaml pubspec.lock ./
COPY core/pubspec.yaml core/
COPY ui/pubspec.yaml ui/
COPY shell/pubspec.yaml shell/
RUN flutter pub get --enforce-lockfile
COPY . .
WORKDIR /workspace/shell
RUN flutter build web --release --base-href "/" --no-tree-shake-icons --no-pub

FROM nginx:1.27-alpine
COPY --from=build /workspace/shell/build/web /usr/share/nginx/html
