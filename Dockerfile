# csharpc with a full host toolchain (clang/lld) so `docker run … csharpc app.cs` works with no
# host install beyond Docker. libLLVM ships inside the release bundle; apt supplies clang 18.
# Build context must include the extracted linux-x64 bundle at docker-bundle/ (see release.yml).
FROM ubuntu:26.04@sha256:d66b366dac61b575bee2aef07e9c26e8070096fbe84a3d4d3eb0da069b5d76db

RUN apt-get update \
 && DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends \
      clang-18 lld-18 ca-certificates \
 && ln -sf /usr/bin/clang-18 /usr/local/bin/clang \
 && ln -sf /usr/bin/ld.lld-18 /usr/local/bin/ld.lld \
 && rm -rf /var/lib/apt/lists/*

COPY docker-bundle/ /opt/csharpc/
ENV PATH="/opt/csharpc:${PATH}"
WORKDIR /work
ENTRYPOINT ["csharpc"]
