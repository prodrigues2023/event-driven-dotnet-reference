# Generic build for any service in the solution. Pass PROJECT (e.g. Ordering, Payments, Shipping).
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG PROJECT
WORKDIR /src
COPY . .
RUN dotnet publish "src/${PROJECT}/${PROJECT}.csproj" -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
ARG PROJECT
WORKDIR /app
COPY --from=build /app ./
ENV APP_DLL=${PROJECT}.dll
ENTRYPOINT ["sh", "-c", "exec dotnet /app/$APP_DLL"]
