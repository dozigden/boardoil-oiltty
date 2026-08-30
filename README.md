# OilTTY

Have you used all of your system memory running a local LLM but still need access to your favourite kanban board? 

OilTTY, the text based interface to BoardOil, is the tool for you. 

Guaranteed smaller memory footprint than Chrome!

## Run

Requires the .NET 10 SDK.

From the repository root:

```sh
dotnet run --project OilTTY
```

OilTTY prompts for your BoardOil login on first run and saves the session for later runs.

To log out:

```sh
dotnet run --project OilTTY -- --logout
```

## Build a self-contained app

For Linux x64:

```sh
dotnet publish OilTTY/OilTTY.csproj \
  -c Release \
  -r linux-x64 \
  --self-contained \
  -p:PublishSingleFile=true \
  -o publish/oiltty-linux-x64
```

Run the published app:

```sh
./publish/oiltty-linux-x64/OilTTY
```

Use a different runtime identifier, such as `linux-arm64`, `win-x64`, or `osx-arm64`, to build for another platform.
