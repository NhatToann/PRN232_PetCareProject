# ---------------------------------------------------------------------------
# LS-12-Docker — Dockerfile multi-stage cho PetShop (slide PRN232 Ch10 S28–30).
#
# Cấu trúc 2 stage theo slide:
#   Stage 1 (build)  : mcr.microsoft.com/dotnet/sdk:8.0   → restore + publish
#   Stage 2 (runtime): mcr.microsoft.com/dotnet/aspnet:8.0 → chạy app
#
# Lệnh dùng kèm (slide Ch10 S29–30):
#   docker build -t petshop-prn232 .
#   docker images
#   docker run -it --rm -p 5286:5286 --name petshopcontainer petshop-prn232
# ---------------------------------------------------------------------------

# ------------------------------- Stage 1: build -------------------------------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Chép file project trước để tận dụng cache layer của Docker khi chỉ đổi code.
COPY PetShop_PRN222.csproj ./
RUN dotnet restore PetShop_PRN222.csproj

# Chép toàn bộ mã nguồn rồi publish ra thư mục /app.
COPY . ./
RUN dotnet publish PetShop_PRN222.csproj -c Release -o /app

# ------------------------------ Stage 2: runtime ------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Container chạy app không cần SDK — chỉ copy kết quả publish.
COPY --from=build /app .

# Cổng ứng dụng (khớp ApplicationUrl http://localhost:5286 trong Properties/launchSettings.json).
EXPOSE 5286

# Cổng gRPC (HTTP/2 h2c thuần) — Kestrel cấu hình trong Program.cs.
EXPOSE 5287

# ASP.NET Core đọc biến môi trường này để bind cổng trong container.
ENV ASPNETCORE_URLS=http://+:5286

# Chạy ứng dụng — container sống theo tiến trình này.
ENTRYPOINT ["dotnet", "PetShop_PRN222.dll"]
