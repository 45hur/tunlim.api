FROM mcr.microsoft.com/dotnet/core/sdk:2.2 AS build-env

RUN mkdir /app
WORKDIR /app

COPY /*.sln ./
COPY /tunlim.api/*.csproj ./tunlim.api/

RUN dotnet restore

RUN dotnet new global.json

COPY / ./
RUN dotnet publish ./tunlim.api.sln -c Release -o out

FROM debian

RUN apt-get update
RUN apt-get install -qq apt-utils
RUN apt-get install -qq libpcap-dev curl wget python perl gpg cron nano arping liblmdb0 liblmdb-dev lmdb-utils

RUN wget -qO- https://packages.microsoft.com/keys/microsoft.asc | gpg --dearmor > microsoft.asc.gpg
RUN mv microsoft.asc.gpg /etc/apt/trusted.gpg.d/
RUN wget -q https://packages.microsoft.com/config/debian/9/prod.list
RUN mv prod.list /etc/apt/sources.list.d/microsoft-prod.list
RUN chown root:root /etc/apt/trusted.gpg.d/microsoft.asc.gpg
RUN chown root:root /etc/apt/sources.list.d/microsoft-prod.list

RUN apt-get install -qq apt-transport-https
RUN apt-get update
RUN apt-get install -qq dotnet-sdk-2.2
RUN export DEBIAN_FRONTEND=noninteractive	
RUN export EDITOR=nano

WORKDIR /app
COPY --from=build-env /app/tunlim.api/out .

ENTRYPOINT ["dotnet", "tunlim.api.dll"]