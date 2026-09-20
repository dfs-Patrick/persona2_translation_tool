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
