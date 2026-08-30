![OilTTY for BoardOil logo](OilTTY-logo.png)

# OilTTY

Have you used all of your system memory running a local LLM but still need access to your favourite kanban board?

You need OilTTY, the text-based client for BoardOil.

Guaranteed smaller memory footprint than Chrome.

![OilTTY board view](OilTTY-board.png)

## Features

### Basics

OilTTY supports basic board navigation and card editing.  More advanced features are out - gotta keep that memory footprint down.

You can switch board, create, move, and edit cards. You'll have to wait for creating tags, types, and slicks though.

### Full slick rendering

Are you a macOS Safari user fed up of your slicks not spanning columns? Good news! Coloured text offers a better experience than Safari! Slicks render fully across columns in OilTTY.

### Light mode

Why should web pages have all the dual theme fun? Light mode, just a ctrl-t away.

![OilTTY board view in light mode](OilTTY-board-light.png)

## Run

Requires the .NET 10 SDK. Currently only tested on Linux, I know, .NET & Linux, weird right?

From the repository root:

```sh
dotnet run --project OilTTY
```

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

Other runtime identifiers, such as `linux-arm64`, `win-x64`, or `osx-arm64`, may work.

## The small print (read this bit really fast)

OilTTY requires a separate [BoardOil](https://github.com/dozigden/boardoil) install. OilTTY stores the selected server and login session in your user application-data directory. Session files contain a refresh token and are restricted to the current user where supported, but are not stored in an operating-system credential vault.
