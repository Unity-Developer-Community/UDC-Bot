# UDC-Bot

A [Discord.NET](https://github.com/discord-net/Discord.Net) bot made for the server Unity Developer Community
Join us on [Discord](https://discord.gg/bu3bbby) !

The code is provided as-is and there will be no guaranteed support to help make it run.

# Table Of Contents
<!-- Link to all the headers -->
- [Compiling](#compiling)
  - [Dependencies](#dependencies)
- [Running](#running)
    - [Docker](#docker)
    - [Runtime Dependencies](#runtime-dependencies)
- [Notes](#notes)
- [FAQ](#faq)

## Compiling

### Dependencies

To successfully compile you will need the following.
- [Discord.Net](https://www.nuget.org/packages/Discord.Net/)
- [Visual Studio](https://visualstudio.microsoft.com/vs/community/) (or IDE of choice)
- [.NET Core SDK](https://www.microsoft.com/net/download/core)
- [Docker](https://www.docker.com/get-started) (Optional, but recommended)

>NOTE: It is highly recommended to install docker and docker-compose to run at least the database in a container for local development.

## Running

Once the above has been done, you'll need to setup a few additional things to get the bot to a functional point.

- Copy the "**Server**" folder to the build location. If you run the bot, it'll inform you where that location is as an error.
- Copy the "**Settings**" to the Server folder, without the "**Deserialized**" folder.
- Make another copy of the "**Settings.example.json**" and rename it to "**Settings.json**"
- Walk through the new "**Settings.json**" file and ensure the Bot Token and "**DbConnectionString**" have been set.

_Several comments have been placed throughout the settings file to indicate what needs changing as well as the settings which aren't currently used._

_If you plan on running the bot outside of the IDE, you will want to give [Discord Net Deployment](https://discord.foxbot.me/docs/guides/deployment/deployment.html) a read._

### Docker
Running docker should be as simple as running `docker-compose up` in the root directory of the project. This will use the docker-compose.yml file to build the bot and database images and run them. You may need to change the DbConnectionString in the [settings.json](https://github.com/Unity-Developer-Community/UDC-Bot/tree/dev/DiscordBot/Settings) file to match the docker-compose.yml file configuration.

> Note: Docker will also create an image of the bot, it may be much quicker to run the bot yourself from the IDE if you're making changes. 
> You can also use `docker-compose up --build` to force a rebuild of the bot image, but I would advise to disable the bot in the docker-compose.yml file, and run the bot from the IDE.

### Runtime Dependencies

If you choose to not use docker, you will need to host the database yourself and ensure the bot can connect to it. You will need an accessible SQL database setup, the easiest way to do this is using XAMPP which can host the database and make it easier to setup.

- [XAMPP](https://www.apachefriends.org/download.html)

If you run the bot now, it will attempt to generate the table for the database. Depending on the permisions of the user, it may fail. You can get around by importing one of the tables below through phpmyadmin.

- ~~**Emptyusers.sql** An empty table that only creates the database structure.~~ (Not yet)
- ~~**Mockusers.sql** A table that creates the database structure, but contains some mock user data as well.~~ (Not yet)

_Once you have imported the database, be sure to create a user in phpmyadmin which can access the database, and the details match your **DbConnectionString**._

On Linux you might need `sudo apt install ttf-mscorefonts-installer` for ImageMagick.

## Notes

I'll re-introduce some of this later.
~~When you hit run, you'll probably see some warnings and errors if you've sped through this without much thought.~~
~~- **_Yellow_** : Warnings _(The bot will continue to run, but may disable some features)_~~
~~- **_Red_** : Errors _(Usually a pending exception/crash is moments away)_~~

I strongly suggest giving [Discord.Net API Documention](https://discord.foxbot.me/stable/api/index.html) a read when interacting with systems you haven't seen before. Discord Net uses Tasks, Asynchronous Patterns and heavy use of Polymorphism, some systems might not always be straight forward.

## FAQ

None yet, ask some, I might add them, additional help isn't garenteed. ~~get gud~~
