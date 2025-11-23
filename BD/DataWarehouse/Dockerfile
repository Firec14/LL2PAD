FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /app

COPY DataWarehouse.csproj .
RUN dotnet restore

COPY . .
RUN dotnet publish -c Release -o /out

FROM mcr.microsoft.com/dotnet/runtime:9.0
WORKDIR /app
COPY --from=build /out .

ENV PORT=8081
ENV ROLE=master

EXPOSE 8081

CMD ["sh", "-c", "dotnet DataWarehouse.dll $PORT $ROLE"]
