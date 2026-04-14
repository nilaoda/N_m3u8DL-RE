FROM mcr.microsoft.com/dotnet/sdk:9.0-alpine

RUN apk add --no-cache bash icu-libs libgcc libstdc++ zlib-dev

WORKDIR /src
COPY . .

RUN dotnet publish src/N_m3u8DL-RE/N_m3u8DL-RE.csproj \
    -c Release \
    -r linux-musl-arm \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:PublishAot=false \
    -p:PublishTrimmed=true \
    -o /app/out

FROM alpine:latest
RUN apk add --no-cache libstdc++ gcompat icu-libs
WORKDIR /app
COPY --from=0 /app/out .

ENTRYPOINT ["./N_m3u8DL-RE"]
