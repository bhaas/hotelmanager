FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY HotelManager.slnx ./
COPY HotelManager.Web/HotelManager.Web.csproj HotelManager.Web/
RUN dotnet restore HotelManager.Web/HotelManager.Web.csproj
COPY . .
RUN dotnet publish HotelManager.Web/HotelManager.Web.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM runtime AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "HotelManager.Web.dll"]
