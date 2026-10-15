# ProcessChecker

A configurable business-process engine. A process (for example Domestic Travel Approval) is defined in a visual designer, not in code. The engine runs any definition: forms, approvals, if/else decisions, supplier emails, uploaded confirmations, links to documents on other servers. Requests can be started by a person or automatically from rows in a database or an API.



## Tech stack
- Backend: .NET 8, C# minimal APIs, one microservice per concern
- Database: Oracle (EF Core, ODP.NET). One schema, tables prefixed `PC_`
- Frontend: React (Vite), React Router, no UI library
- Gateway: YARP reverse proxy with JWT
- Login: Active Directory (`PrincipalContext`, Windows only) plus one local admin account

## Services

| Service | Port | Purpose |
|---|---|---|
| ApiGateway | 5000 | Login (AD or local), JWT, roles, routes `/api/*`, people search |
| DefinitionService | 5001 | Versioned process definitions (JSON) |
| EngineService | 5002 | Runs requests, step handlers, polling, tasks, access rules, dashboards, document proxy |
| ConnectorService | 5003 | Named data sources (Oracle, SQL Server, MySQL, PostgreSQL, Access, JSON, CSV, REST API) |
| NotificationService | 5004 | SMTP email and send log |
| DocumentService | 5005 | Uploaded files on disk, metadata in Oracle, per-request access check |
| Web app (Vite) | 5173 | React UI, proxies `/api` to the gateway |

`ProcessChecker.Contracts` is the shared project: definition models, `InternalAuth` (gateway-key and identity headers), `DbObjects` (checks what exists before creating tables).

## Repo layout
```
ProcessChecker.sln
project.md
start-all.ps1
src/
  ProcessChecker.Contracts/
  ApiGateway/            auth, AD, users, YARP routes
  DefinitionService/
  EngineService/         engine, handlers, conditions, polling, access, dashboards
  ConnectorService/      connectors for each data source type
  NotificationService/
  DocumentService/
  web/src/
    pages/                 Dashboard, Tasks, Requests, Processes, InstanceDetail,
                         Dashboards, Connections, Users, Account, Login
    designer/              drag-and-drop process designer (canvas, panels, if/else lanes)
    links.js  UserPicker.jsx  Viewer.jsx  ApiFields.jsx  ui.jsx  api.js  user.jsx
```

## What users can do
- Sign in with their Windows (AD) username and password.
- Start a request from a process card, or have it started automatically.
- **My Tasks:** tasks assigned to them, requests made for them, requests they observe, tasks nobody is assigned to.
- **Requests:** every request they can see, with filters, progress, and a Documents button.
- **Request page:** step progress, action panel (approve, reject, form, upload, retry), documents and files with an in-page viewer, observers, why a step was skipped or the request ended.
- **Overview and My dashboards:** counts and charts. Each person designs their own (for example only Domestic Travel: how many came, how many completed).
- **Admins** also design processes, manage data sources and users, assign or re-assign tasks, and see everything.

## Process definition
Stored as JSON, edited in the designer. Every save creates a new version; running requests keep the version they started with.
```json
{
  "key": "travel", "name": "Domestic Travel",
  "trigger": { "type": "DatabasePoll", "connection": "HR_DB", "schema": "HR",
               "query": "SELECT * FROM TRAVEL WHERE STATUS='NEW'", "keyColumn": "ID", "everyMinutes": 5 },
  "requesters": ["{{USERNAME}}"],
  "observers": ["hr.head@company.com"],
  "links": [ { "label": "Open the report",
               "url": "https://server:9012/Document/PrintReport/{{id}}?mid={{id+1}}&sid={{id+2}}&Type=M" } ],
  "steps": [ ... ]
}
```
- `{{field}}` works in approvers, assignees, observers, requesters, e-mail text, link addresses, reasons and condition values. `{{id+1}}` adds or subtracts a whole number.
- **Requesters** are the person the request is for (a username or email, taken from a field). They can see the request and its documents but cannot act. If the data only has an ID, add a Fetch data step that looks up the username.

### Step types
| Type | Behaviour |
|---|---|
| Form | Dynamic fields (text, text area, number, date, dropdown, checkbox), optional attachment, optional assignees |
| Approval | Emails approvers. By default one approval completes the step; tick "Every approver must approve" to require all. One rejection rejects the request |
| Upload | A person uploads a file (PDF, image, Word, Excel, CSV, TXT, max 20 MB), optional assignees |
| DataFetch | Runs a query on a data source (optional schema). The first row becomes request data |
| Dispatch | Emails the collected data (or a message) to a supplier |
| Notify | Sends an email |
| Decision (If / Else) | Evaluates conditions once and saves `{{key.result}}`. Steps in the "If true" lane run when true, steps in the "Else" lane when false. Lanes can nest |
| End | Closes the request on that path as Completed, Rejected or Cancelled, with an optional reason. Every step still waiting is skipped |
| Link (legacy) | Old step that built a link inside the engine. Not offered in the designer; old processes still run |

- **Run this step only if ** can be set on any step outside a lane (simple single-step rule). Skipped steps count as done in progress bars and show the reason.
- Conditions compare a field with a value: equals, not equals, greater/less (numbers and dates), contains, is one of, is empty, is filled in. Match "all" or "any".

## Automatic start (polling)
EngineService wakes every 30 seconds and checks each active process whose interval has passed. It runs the trigger query through ConnectorService, takes the key column as the row id, and starts one request per row it has not seen (`PC_TRIGGER_SEEN`). The source is only read. The query must filter to new rows only. Admins can test with `POST /api/poll/{key}/run-now`.

## Data sources
Registered once by name under Connections and referenced by name in definitions.
- **SQL types** (Oracle, SQL Server, MySQL, PostgreSQL, Access): a single SELECT, `:name` parameters. Oracle and PostgreSQL support several schemas per connection, picked in the process or taken from a form field.
- **JSON / CSV files:** JSONPath, or a simple filter. Files must be inside `FileRoots`.
- **REST API:** fixed base address, optional sign-in (basic, bearer, API key, Windows), optional POST. The query is `GET /path?x=:value`, optionally `-> $.data`, header lines, and a JSON body for POST. Redirects are refused, answers are capped at 5 MB.
- Connection strings and API secrets are encrypted (ASP.NET Data Protection; the `keys/` folder is git-ignored and must never be committed).

## Documents
- **Uploaded files:** stored under `Storage:StoragePath` (keep it outside the data-source folders). Downloads need access to the request: admin, the uploader, or anyone who can see the request. Adding a file needs a role that can work on the request. If EngineService is down, downloads fail closed.
- **Document links:** built from the request data. The **View** button makes EngineService fetch the document and hand it to the browser, so it works even when the other server refuses to be framed. Only hosts in `Documents:AllowedHosts` are fetched. PDFs, images and text show in the pop-up; other types are offered as a download. "Open in new tab" always works.

## Roles and access
- **Admin:** a member of an AD group in `Ad:AdminGroups`, or the local admin.
- **User:** everyone else.
- A person sees a request only if they started it, are an assignee, approver, observer or requester, or are an admin. This applies to the list, the request page, My Tasks, all dashboards and file downloads. The data lives in `PC_PARTICIPANTS`.
- Observers and requesters can only look. A Form or Upload step with no assignees can be done only by the person who started the request, or an admin.

## Authentication
- AD users sign in with a plain username. The AD `mail` attribute must match the addresses used in assignments and approvals; the AD username matches requesters that are usernames.
- The gateway issues a JWT (8 h) and re-checks the user on each request (cached 60 s).
- Every service rejects calls without the shared key header, so the browser must go through the gateway.

## Configuration
Keep secrets out of committed files.
| Setting | Where |
|---|---|
| `Gateway__Key` (40+ chars) | user environment variable, same value for every service |
| `Jwt:Key` (48+ chars), `Auth:AdminPassword` | `dotnet user-secrets` in `src/ApiGateway` |
| `Ad:Enabled`, `Ad:Domain`, `Ad:AdminGroups` | ApiGateway `appsettings.json` |
| `Smtp:*` (Host, Port, FromEmail, FromName, EnableSsl, Security, DryRun) | NotificationService `appsettings.json`; password via user-secrets |
| `Storage:StoragePath`, `MaxSizeMb`, `Services:Engine` | DocumentService `appsettings.json` |
| `Documents:AllowedHosts`, `UseWindowsLogin`, `IgnoreCertificateErrors`, `Web:BaseUrl` | EngineService `appsettings.json` |
| `ConnectionStrings:Default` | each service's `appsettings.json` |

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
Open http://localhost:5173. The first local admin is `Auth:AdminEmail` with the `Auth:AdminPassword` secret, created only when `PC_USERS` is empty. Each service creates only the tables and columns it is missing at startup (no error output on a normal start).

## Database objects
`PC_DEFINITIONS`, `PC_CONNECTIONS`, `PC_INSTANCES` (incl. `OBSERVERS`, `REQUESTER`), `PC_STEPS` (incl. `ASSIGNEES`), `PC_APPROVALS`, `PC_TRIGGER_SEEN`, `PC_PARTICIPANTS`, `PC_DASHBOARDS`, `PC_NOTIFICATIONS`, `PC_DOCUMENTS`, `PC_USERS`.

## Smoke test after a deployment
1. Health: `GET /api/health` on the gateway returns ok.
2. AD sign-in works; the local admin still works.
3. Start a request, fill a form, approve as a second user, upload a file.
4. Another user not on the request gets "not found" for the request and its file.
5. If/Else: one request takes each lane; the lane not taken shows Skipped with the reason.
6. End step: the request closes at once and nothing stays open.
7. Automatic start: `run-now` creates requests once and skips them on the second call.
8. Test email from the log page or `POST /api/notifications/test`.

## Troubleshooting
| Symptom | Check |
|---|---|
| 502 on a page | a backend service is not running |
| 401 "Call this service through the gateway" | a service port was opened directly, or the key differs |
| 404 on an action or a file | the person lacks access, or the endpoint is missing |
| Email fails with a STARTTLS error | set `Smtp:Security` to `None` for a plain relay |
| Link says "waiting for id" | open "Collected data" on the request to see the real column name |
| Document pop-up shows an error | the message says why: host not in `AllowedHosts`, redirected to a sign-in, access refused |
| AD sign-in fails | gateway console line "Active Directory sign-in is ON/OFF"; `Ad:Domain`; gateway must run on Windows |
| Requester cannot see a request | check the requester value in "Collected data"; run "Apply to existing requests" in the designer |

## Known limitations
- Steps run one after another (with if/else lanes). No parallel steps and no loops.
- Assignees are people, not AD groups. No due dates or reminders.
- Automatic requests have no starter, so their unassigned Form or Upload steps are admin-only. Always assign people on those steps.
- A requester set by username gets no emails (no address). Dispatch emails cannot carry attachments.
- Dashboards are private to their owner. Notification log rows are never deleted.
- There are no automated tests.

## Done
- [x] Services, gateway, designer, polling, data sources (incl. REST API), documents, notifications
- [x] AD login, task assignment, observers, requesters, visibility rules, personal dashboards
- [x] If/Else lanes, End step, per-step conditions, "all must approve"
- [x] Server-side document viewer, per-request file access checks

## Pending (recommended order)
1. Due dates and reminders on steps, with overdue marking in My Tasks
2. Email to the requester or others when a file is uploaded or the request ends
3. Assign tasks to AD groups
4. Attachments in Dispatch emails
5. Parallel steps
6. Notification log retention cleanup
7. EF migrations, Docker Compose, HTTPS, secret management, backups
8. Remaining redesign: Connections and Users pages, mobile-friendly menu
9. Automated tests (engine rules, conditions, access)