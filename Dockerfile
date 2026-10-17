# Online server for the chess app. Works on Render, Railway, Fly.io and similar hosts:
# they set PORT, the server listens on it and clients connect to wss://<host>/ws.
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app
COPY Directory.Build.props ./
COPY src/Chess.Core/ src/Chess.Core/
COPY src/Chess.Server/ src/Chess.Server/
RUN dotnet publish src/Chess.Server/Chess.Server.csproj -c Release -o /out

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /out ./
ENV PORT=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Chess.Server.dll"]
