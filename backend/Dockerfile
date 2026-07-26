FROM maven:3.9-eclipse-temurin-21 AS access-build
WORKDIR /access-reader
COPY access-reader/pom.xml ./
COPY access-reader/src ./src
RUN mvn -q package

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY Library.Api.csproj ./
RUN dotnet restore
COPY . ./
COPY --from=access-build /access-reader/target/access-reader.jar ./access-reader.jar
RUN dotnet publish -c Release -o /app/publish --no-restore

FROM eclipse-temurin:21-jre AS java

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
COPY --from=java /opt/java/openjdk /opt/java/openjdk
COPY --from=build /app/publish ./
ENV JAVA_HOME=/opt/java/openjdk
ENV PATH="${JAVA_HOME}/bin:${PATH}"
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Library.Api.dll"]