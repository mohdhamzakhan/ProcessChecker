# ProcessChecker

A configurable business-process engine. A process (for example Business Travel Approval) is defined as JSON in a visual designer, not in code. The engine runs any definition: it waits for approvals, shows forms, emails suppliers, collects uploaded confirmations, builds links to documents on other servers, and can start requests automatically from rows in a database.

Repo: https://github.com/mohdhamzakhan/ProcessChecker (branch `master`)

## Tech stack
- Backend: .NET 8, C# minimal APIs, one microservice per concern
- Database: Oracle (EF Core, ODP.NET). One schema, tables prefixed `PC_`
- Frontend: React (Vite), React Router, no UI library
- Gateway: YARP reverse proxy with JWT
- Login: Active Directory (`PrincipalContext`, Windows only) plus one local admin account

## Services

| Service | Port | Purpose |
|---|---|---|
| ApiGateway | 5000 | Login (AD or local), JWT, roles, routes `/api/*` to services, people search |
| DefinitionService | 5001 | Versioned process definitions (JSON) |
| EngineService | 5002 | Runs requests, step handlers, polling, tasks, visibility rules, dashboards |
| ConnectorService | 5003 | Named data sources (Oracle, SQL Server, MySQL, PostgreSQL, Access, JSON, CSV) |
| NotificationService | 5004 | SMTP email and send log |
| DocumentService | 5005 | Stores uploaded files on disk, metadata in Oracle |
| Web app (Vite) | 5173 | React UI, proxies `/api` to the gateway |

`ProcessChecker.Contracts` is the shared project (definition models, the gateway-key helper `InternalAuth`, `DbObjects` for safe table creation).

## Repo layout
```
ProcessChecker.sln
project.md
start-all.ps1
src/
  ProcessChecker.Contracts/   DefinitionDto, InternalAuth, DbObjects
  ApiGateway/                 auth, AD, users, YARP routes
  DefinitionService/
  EngineService/              engine, handlers, polling, access, dashboards
  ConnectorService/
  NotificationService/
  DocumentService/
web/
  src/
    pages/                    Dashboard, Tasks, Requests, Processes, InstanceDetail,
                              Dashboards, Connections, Users, Account, Login
    designer/                 drag-and-drop process designer
    links.js, UserPicker.jsx, Viewer.jsx, ui.jsx, api.js, user.jsx
```

## What users can do
- **Sign in** with their Windows (AD) username and password.
- **Start a request** from a process card, or have it start automatically from a database check.
- **My Tasks:** tasks assigned to them, requests they observe, tasks nobody is assigned to.
- **Requests:** every request they can see, with filters, progress, and a Documents button.
- **Request page:** step-by-step progress, action panel (approve, reject, form, upload, retry), documents and files with an in-page viewer, observers.
- **Overview and My dashboards:** counts and charts. Each user designs their own dashboards (for example only Domestic Travel: how many came, how many completed).
- **Admins** also: design processes, manage data sources and users, assign or re-assign tasks, see everything.

## Process definition
Stored as JSON and edited in the designer.
```json
{
  "key": "travel", "name": "Domestic Travel",
  "trigger": { "type": "DatabasePoll", "connection": "HR_DB", "schema": "HR",
               "query": "SELECT * FROM TRAVEL WHERE STATUS='NEW'", "keyColumn": "ID", "everyMinutes": 5 },
  "observers": ["hr.head@company.com"],
  "links": [ { "label": "Open the report",
               "url": "https://server:9012/Document/PrintReport/{{id}}?mid={{id+1}}&sid={{id+2}}&Type=M" } ],
  "steps": [ { "key": "approval", "name": "Manager approval", "type": "Approval", "approvers": ["{{managerEmail}}"] } ]
}
```
- Every save of a key creates a new version. Running requests keep the version they started with.
- `{{field}}` placeholders work in approvers, assignees, observers, e-mail text and links. `{{id+1}}` adds or subtracts a whole number.

### Step types
| Type | Behaviour |
|---|---|
| Form | Dynamic fields (text, text area, number, date, dropdown, checkbox), optional attachment, optional assignees |
| Approval | Emails approvers. One approval completes the step, one rejection rejects the request |
| Dispatch | Emails the collected data (or a message) to a supplier |
| Upload | A person uploads a file (PDF, image, Word, Excel, CSV, TXT, max 20 MB) |
| DataFetch | Runs a query on a data source (optional schema); the first row becomes request data |
| Notify | Sends an email |

Steps run strictly one after another (no branching or parallel steps yet).

## Automatic start (polling)
EngineService wakes every 30 seconds and checks each active process whose interval has passed. It runs the trigger query through ConnectorService, takes the key column as the row id, and starts one request per row it has not seen (`PC_TRIGGER_SEEN`). The source database is only read. Queries must filter to new rows only. Admins can test with `POST /api/poll/{key}/run-now`.

## Data sources and schemas
Registered once by name under Connections and referenced by name in definitions. Connection strings are encrypted (ASP.NET Data Protection; the `keys/` folder is git-ignored and must never be committed). SQL queries are limited to a single SELECT. For Oracle and PostgreSQL a connection can allow several schemas; a process picks one, or takes it from a form field.

## Documents and links
- Uploaded files are stored under the folder in `Storage:StoragePath` (keep it outside the data-source folders).
- Document links are built in the browser from the request data and shown on the request page and from the Documents button in the Requests list.

## Roles and access
- **Admin:** from an AD group listed in `Ad:AdminGroups`, or a local admin account.
- **User:** everyone else.
- A person sees a request only if they started it, are an assignee or approver, or an observer (or are an admin). This applies to the list, the request page, My Tasks and all dashboards. The data lives in `PC_PARTICIPANTS`.
- **Observers** can follow a request but cannot act. Observers can be set on the process, or changed on a request by an admin or the person who started it.
- A Form or Upload step with no assignees can be done only by the person who started the request, or an admin.

## Authentication
- AD users sign in with a plain username. The AD `mail` attribute must match the addresses used in assignments and approvals.
- The gateway issues a JWT (8 h). Every request re-checks the user (cached 60 s).
- All services reject calls without the shared key header (`Gateway:Key`), so the browser must go through the gateway.

## Configuration
Keep secrets out of committed files.
| Setting | Where |
|---|---|
| `Gateway__Key` (40+ chars) | user environment variable, the same value for every service |
| `Jwt:Key` (48+ chars), `Auth:AdminPassword` | `dotnet user-secrets` in `src/ApiGateway` |
| `Ad:Enabled`, `Ad:Domain`, `Ad:AdminGroups` | `src/ApiGateway/appsettings.json` |
| `Smtp:*` (Host, Port, FromEmail, FromName, EnableSsl, Security, DryRun) | `src/NotificationService/appsettings.json`; password via user-secrets |
| `Storage:StoragePath`, `MaxSizeMb` | `src/DocumentService/appsettings.json` |
| `ConnectionStrings:Default` | each service's `appsettings.json` |
| `Web:BaseUrl` | `src/EngineService/appsettings.json` (links in emails) |

## Run locally
```powershell
setx Gateway__Key "a-long-random-string-of-40-plus-characters"     # then reopen the terminal
cd src\ApiGateway
dotnet user-secrets set "Jwt:Key" "a-random-string-of-48-or-more-characters"
dotnet user-secrets set "Auth:AdminPassword" "ChangeMe-Admin-123"
cd ..\..
dotnet build
cd web; npm install; cd ..
.\start-all.ps1
```
Open http://localhost:5173. The first local admin is `Auth:AdminEmail` with the `Auth:AdminPassword` secret, created only when `PC_USERS` is empty. Each service creates only the tables or columns it is missing at startup.

## Database objects
`PC_DEFINITIONS`, `PC_CONNECTIONS`, `PC_INSTANCES`, `PC_STEPS`, `PC_APPROVALS`, `PC_TRIGGER_SEEN`, `PC_PARTICIPANTS`, `PC_DASHBOARDS`, `PC_NOTIFICATIONS`, `PC_DOCUMENTS`, `PC_USERS`.

## Troubleshooting
| Symptom | Check |
|---|---|
| 502 on a page | a backend service is not running |
| 401 "Call this service through the gateway" | a service port was opened directly, or the key differs |
| 404 on a retry or action | the engine endpoint is missing, or the person lacks access |
| Email fails with STARTTLS error | set `Smtp:Security` to `None` for a plain relay |
| Link says "waiting for id" | open "Collected data" on the request to see the real column name |
| AD sign-in fails | gateway console line "Active Directory sign-in is ON/OFF"; `Ad:Domain`; gateway must run on Windows |

## Known limitations
- Sequential steps only, no branching or parallel steps.
- "Any one approver" rule only (no "all must approve").
- Uploaded files can be downloaded by anyone signed in who has the file id (no per-request check yet).
- Task assignees are people, not AD groups. No due dates or reminders.
- Dashboards are private to their owner.
- Automatic requests have no starter, so their unassigned Form or Upload steps are admin-only. Always assign people on those steps.

## Roadmap
- [x] Services, gateway, designer, polling, data sources, documents, notifications
- [x] AD login, task assignment, observers, visibility rules, personal dashboards
- [ ] Per-request check on file downloads
- [ ] Decision (if/else) and parallel steps
- [ ] Assignment to AD groups, due dates, reminders
- [ ] EF migrations instead of startup checks, Docker Compose