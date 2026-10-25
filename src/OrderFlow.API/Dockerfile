FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first for better layer caching
COPY OrderFlow.sln ./
COPY src/OrderFlow.Domain/OrderFlow.Domain.csproj src/OrderFlow.Domain/
COPY src/OrderFlow.Application/OrderFlow.Application.csproj src/OrderFlow.Application/
COPY src/OrderFlow.Infrastructure/OrderFlow.Infrastructure.csproj src/OrderFlow.Infrastructure/
COPY src/OrderFlow.API/OrderFlow.API.csproj src/OrderFlow.API/
RUN dotnet restore src/OrderFlow.API/OrderFlow.API.csproj

COPY src/ src/
RUN dotnet publish src/OrderFlow.API/OrderFlow.API.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
COPY --from=build /app/publish .
# Non-root user (provided by the official image)
USER $APP_UID
ENTRYPOINT ["dotnet", "OrderFlow.API.dll"]
