FROM archlinux:base AS dependencies
RUN pacman -Syu --noconfirm --needed nodejs npm ca-certificates \
    && pacman -Scc --noconfirm

FROM dependencies AS build
WORKDIR /opt/p2-tool
COPY package.json package-lock.json ./
RUN npm ci
COPY tsconfig.json main.ts ./
COPY cli ./cli
COPY lib ./lib
COPY game ./game
COPY fonts ./fonts
COPY tests ./tests
RUN npx tsc && node --test dist/tests/font_profile.test.js

FROM dependencies AS runtime
ENV NODE_ENV=production
WORKDIR /opt/p2-tool
COPY package.json package-lock.json ./
RUN npm ci --omit=dev && npm cache clean --force
COPY --from=build /opt/p2-tool/dist ./dist
COPY --from=build /opt/p2-tool/game ./game
COPY --from=build /opt/p2-tool/fonts ./fonts
RUN groupadd --gid 1000 p2tool \
    && useradd --uid 1000 --gid p2tool --create-home p2tool \
    && mkdir -p /lab/iso \
    && chown -R p2tool:p2tool /lab
USER p2tool
WORKDIR /
ENTRYPOINT ["node", "/opt/p2-tool/dist/cli/mod.js"]
CMD ["--help"]

FROM dependencies AS extension-build
WORKDIR /extensions/editor
COPY vscode-extension/package.json vscode-extension/package-lock.json ./
RUN npm ci
COPY vscode-extension ./
RUN npm test && npm run package
WORKDIR /extensions/host
COPY vscode-ppsspp-host ./
RUN ../editor/node_modules/.bin/vsce package --no-dependencies --skip-license -o p2-ppsspp-host.vsix

FROM runtime AS development
USER root
RUN pacman -Syu --noconfirm --needed git tar gzip openssh curl \
    && pacman -Scc --noconfirm \
    && rmdir /lab/iso /lab \
    && ln -s /workspaces/p2-tool/lab /lab
COPY --from=extension-build /extensions/editor/p2-tbf-editor.vsix /opt/p2-tool/vscode/p2-tbf-editor.vsix
COPY --from=extension-build /extensions/host/p2-ppsspp-host.vsix /opt/p2-tool/vscode/p2-ppsspp-host.vsix
WORKDIR /workspaces/p2-tool
ENTRYPOINT []
CMD ["sleep", "infinity"]
