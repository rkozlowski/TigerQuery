# TigerSqlCmd privacy statement

This statement covers TigerSqlCmd — the `tiger-sqlcmd` command — as released in one
version, in both of its distributions: the machine-wide Windows installer (from GitHub
Releases, or through WinGet) and the `ItTiger.TigerSqlCmd` .NET tool (from NuGet.org), which
also runs on Linux and macOS. It describes what TigerSqlCmd stores on your computer, what it
sends and to whom, and how to remove what it keeps.

Each release publishes the statement that applies to it, and that copy never changes after
the release: the statement for version *X* is the `PRIVACY.md` attached to that version's
GitHub Release, `https://github.com/rkozlowski/TigerQuery/releases/download/vX/PRIVACY.md`.
The Windows installer also installs it as `PRIVACY.md` in TigerSqlCmd's folder. A later
version may carry a different statement when its behavior changes.

TigerSqlCmd is published by IT Tiger (<https://www.ittiger.net/>).

TigerSqlCmd is a command-line client for Microsoft SQL Server. It has no accounts,
telemetry, analytics, advertising, crash reporting of its own, license checks, or update
checks. It does not send your connections, credentials, queries, results, or logs to IT
Tiger or to anyone else. It connects only to the SQL Servers your saved connections name,
starts other programs only when you tell it to, and its Windows installer downloads the
Microsoft .NET runtime only when it is missing. Each of these is described below.

The ItTiger.TigerQuery libraries that TigerSqlCmd is built on are also published on
NuGet.org for other applications to use. This statement covers TigerSqlCmd; an application
that uses those libraries is responsible for its own privacy statement.

## What TigerSqlCmd stores on your computer

### Saved connections

TigerSqlCmd keeps the connections you save with `tiger-sqlcmd connection` in one file, the
connection store:

- on Windows, `%APPDATA%\ItTiger.net\sqlserver-connections.json`;
- on Linux and macOS, `~/.config/ItTiger.net/sqlserver-connections.json`.

A run uses a different file when you name one with `--tq-connection-store-file` or the
`TIGERQUERY_CONNECTION_STORE_FILE` environment variable. The default store is shared: other
IT Tiger tools that work with SQL Server connections read and write the same file, so a
connection saved in one is available in the others.

For each saved connection the store can hold:

- the connection's name, the SQL Server host or instance, and the initial database;
- how it authenticates (Windows integrated authentication or a SQL Server login), and the
  SQL Server login name;
- the protected password described under **Passwords and other credentials**;
- connection settings: encryption and certificate trust, application intent, timeouts,
  pooling, and other connection options you add (options that carry a password or an
  access token are refused);
- references to values kept elsewhere — the name of an environment variable, or the path
  of a file and a key within it — but never the values they point to;
- labels you attach with `--metadata`, and, for connections that TigerSqlCmd's end-to-end
  test commands create, the test session's identifier and the name of the test database.

Server, database, and login names can identify you or your organization. The store records
no history of when or how often a connection was used, and no queries or results.

While it changes the store, TigerSqlCmd briefly writes two files beside it: a `.lock` file
that keeps another program from changing the store at the same moment, and a temporary
copy that replaces the store in one step. Both are deleted when the change completes; if
TigerSqlCmd is stopped in the middle of a change, the temporary copy can remain beside the
store until you delete it.

### Passwords and other credentials

TigerSqlCmd asks for a SQL Server password only at a masked prompt; it never accepts one as
a command-line argument.

- **On Windows**, a password you enter is encrypted with Windows Data Protection (DPAPI) for
  your Windows account before it is written to the store. Only your Windows account can
  decrypt it — on another computer, only through a roaming profile of that same account.
  TigerSqlCmd decrypts it in memory when it connects and never writes it in plain text,
  displays it, or logs it. If encryption fails, nothing is saved, and if a stored password
  cannot be decrypted — for example in a store copied from another account or computer —
  TigerSqlCmd says so instead of trying to sign in without it.
- **On Linux and macOS** there is no DPAPI, so TigerSqlCmd does not save a typed password:
  it refuses to save such a connection and asks you to use a reference instead.
- **References.** A connection can instead read its password, login name, server, database,
  or complete connection string from an environment variable or a file you name. TigerSqlCmd
  reads the value each time it connects and never copies it into the store, a log, or its
  output; protecting that variable or file is up to you.

To remove a saved credential, delete the connection with
`tiger-sqlcmd connection delete <name>`, or change it with `tiger-sqlcmd connection edit`.

### Queries, results, and messages

TigerSqlCmd reads the SQL you type or the script files you name, and sends it to the SQL
Server of the connection you choose. It shows results and SQL Server messages in your
terminal. With `-o`/`--output`, `-e`/`--error-output`, or a script's `:Out` and `:Error`
directives, it writes result sets and messages to the files you name instead. Each run
replaces an existing file of the same name rather than adding to it. TigerSqlCmd keeps no
other copy of your queries or results, and no history of the commands you run.

Results and messages are your data, written as SQL Server returned them, so they can
contain anything your database holds.

### Logs and diagnostic output

TigerSqlCmd writes a log only when you name a file with `--log-file`. The log records the
run's progress, SQL Server messages — including the text of `PRINT` and `RAISERROR`
statements — and failures, with the SQL Server error number and the type and message of the
error and of the errors that caused it. It never records connection strings, passwords, the values of `-v` variables, or
the text of your queries, although a SQL Server error message can quote part of a query —
`Incorrect syntax near ...`, for example — as SQL Server words it. A log is added to on each
run that names it and is never deleted by TigerSqlCmd.

With `--verbosity Verbose` or `VeryVerbose`, TigerSqlCmd also prints the query text and the
values of the `-v` variables you pass to your terminal, where whatever captures the output —
a build system's job log, for example — can keep them. Do not pass secrets as `-v`
variables.

Error messages name saved connections, servers, and files, but never contain a password or
a connection string.

Apart from the files described here, TigerSqlCmd itself writes no files, registry values,
or event-log entries.

### What the Windows installer records

The installer keeps a record of the installation in `%ProgramData%\TigerSetup\ItTiger.TigerSqlCmd`:
the files, the `PATH` entry, and the shortcut it installed, the installation's settings, a
copy of its uninstaller, and a log of each installation, upgrade, and repair. The logs name
the files and settings the installer changed and the Windows paths it used; they contain
nothing from your connections, queries, or results. A log you ask for with `--log` is written
where you name it and is yours to keep or delete. Uninstalling removes the record and its
logs.

## Network communication

**SQL Server.** TigerSqlCmd connects to the server a saved connection names, using
Microsoft's SQL Server client library (Microsoft.Data.SqlClient). Over that connection it
sends:

- your sign-in: the SQL Server login name and password, or, with Windows integrated
  authentication, your Windows account through Windows' own sign-in protocols;
- the information SQL Server's login protocol carries for every client, which SQL Server
  can record and display: your computer's name, the client application's name, and its
  process ID;
- the SQL you run, and the values you give `-v` variables wherever your SQL uses them.

Whether the connection is encrypted follows the connection's encryption setting.
Connections created with `tiger-sqlcmd connection add` require encryption unless you choose
otherwise. What the server keeps — for example audit records or query history — is decided
by the server's configuration, not by TigerSqlCmd. Connection options you add yourself can
make the client library contact other services you name, such as an enclave attestation
service for Always Encrypted.

**End-to-end test commands.** The `e2e` commands create and drop databases on a SQL Server
only through a connection you have explicitly authorized for that purpose, and remove only
the databases they created.

**Programs you start with `exec`.** `tiger-sqlcmd exec` starts the program you name and
hands it a saved connection's complete connection string, including its password, in an
environment variable you name, in its arguments, or both. That program then has your
credentials, and what it does with them is governed by that program. A value in a
program's arguments can be seen by other users and programs on the same computer that can
list running processes; the environment variable is the safer choice when the program
supports it. TigerSqlCmd does not log or display the connection string it hands over.

**The Windows installer** makes a network request only if the Microsoft .NET 10 Runtime is
missing. It then downloads Microsoft's runtime installer from Microsoft — from the address
recorded when TigerSqlCmd's installer was built, or, when that record is out of date, from
the address the WinGet community catalog gives, which it reads from Microsoft's catalog
servers — and runs it only if its SHA-256 matches the published one. Started with
`--no-dependency-install`, or without a network connection, it stops instead and changes
nothing. These requests carry what any download carries, such as your computer's IP address,
and nothing about you or your use of TigerSqlCmd. WinGet installs the runtime itself before
running the installer.

**Downloading TigerSqlCmd.** GitHub, NuGet.org, WinGet, and the .NET SDK (for
`dotnet tool install`) handle your download as they handle any other, under their own
operators' privacy terms. IT Tiger receives no information from them about you.

Nothing else: TigerSqlCmd makes no other network connection.

## Uninstalling and upgrading

**Uninstalling** the Windows installation — from **Settings > Apps**, with its uninstaller,
or with `winget uninstall` — removes the program files under
`%ProgramFiles%\TigerSqlCmd`, its Start menu shortcut, its entry in the system `PATH`, its
Add/Remove Programs entry, and the installer's record of the installation. A file in the
program folder that was changed after installation is kept and reported.
`dotnet tool uninstall` removes the .NET tool.

Neither removes your data, deliberately:

- the connection store stays, because other IT Tiger tools may use the same connections,
  and removing them would break those tools;
- your SQL scripts, result and message files, and log files stay where you created them;
- no SQL Server database is changed or removed, including databases created by the `e2e`
  commands;
- a .NET Runtime the installer added stays, because other applications may use it.

To remove your saved connections and their protected passwords, delete them with
`tiger-sqlcmd connection delete` before uninstalling, or delete the store file afterwards.
Each Windows account that used TigerSqlCmd has its own store.

**Upgrading or reinstalling** replaces the program files and keeps your saved connections,
including their protected passwords, and all of your files. Upgrading from TigerSqlCmd 0.8.8
or earlier first removes that installation, under `%ProgramFiles%\ItTiger\TigerSqlCmd`, with
its own uninstaller; that removes only what the earlier installer installed.

## Your choices

- Use Windows integrated authentication, or password references, if you prefer that no
  password be saved by TigerSqlCmd at all.
- Leave `--log-file` and the verbose levels off when you do not need them, and delete logs
  and result files when you no longer need them.
- Delete individual connections with `tiger-sqlcmd connection delete`, or the whole store
  file.

## Questions

Report questions or concerns about this statement at
<https://github.com/rkozlowski/TigerQuery/issues>.
