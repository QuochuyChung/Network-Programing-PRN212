# ---- Build stage ----
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy csproj truoc de cache lop restore (chi restore lai khi doi package, khong phai moi lan sua code)
COPY ChatProtocol/ChatProtocol.csproj ChatProtocol/
COPY ChatServer.Data/ChatServer.Data.csproj ChatServer.Data/
COPY ChatServer/ChatServer.csproj ChatServer/
RUN dotnet restore ChatServer/ChatServer.csproj

# Copy toan bo code roi build
COPY ChatProtocol/ ChatProtocol/
COPY ChatServer.Data/ ChatServer.Data/
COPY ChatServer/ ChatServer/
RUN dotnet publish ChatServer/ChatServer.csproj -c Release -o /app/publish --no-restore

# ---- Runtime stage ----
FROM mcr.microsoft.com/dotnet/runtime:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# Port 5000: giao thuc chat (JSON, length-prefix)
# Port 5001: FileTransferServer rieng (upload/download anh + file lon, mac dinh = port chat + 1)
EXPOSE 5000
EXPOSE 5001

ENTRYPOINT ["dotnet", "ChatServer.dll"]
