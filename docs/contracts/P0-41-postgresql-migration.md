# P0-41 — ย้าย backend จาก SQL Server ไป PostgreSQL

```
Status: IMPLEMENTED
Owner:  Claude Code (infra บน VPS + implementation)
Date:   2026-09-05
```

> **อัปเดต 2026-09-05 — งานนี้ Claude Code ลงมือทำเองจนจบแล้ว ไม่ได้ส่ง Antigravity**
> เอกสารนี้เก็บไว้เป็นบันทึกการตัดสินใจและ checklist สำหรับรีวิว ไม่ใช่คำสั่งที่รอคนทำอีกต่อไป
>
> **ผลจริง:** build 0 warning/0 error · unit **774/774** · architecture **5/5** · migration
> `InitialCreatePostgres` apply ผ่านจริงบน PostgreSQL 17 (ทดสอบบน database ชั่วคราวบน VPS แล้วลบทิ้ง
> ไม่แตะ `SIRIUPSKILL`) ได้ครบ 65 ตาราง 10 schema **ตรงกับ migration เดิมเป๊ะ** · cascade FK 19 ตัวเท่าเดิม
> · ค้นหาภาษาไทยผ่าน `pg_trgm` ทำงานจริง (เทียบแล้ว `tsvector` ได้ 0 match ตามที่คาดไว้ใน §5.1)
>
> ### 3 จุดที่ของจริงไม่ตรงกับที่เขียนไว้ในแผน — build เป็นคนจับได้
> 1. **§3.2 Hangfire** — overload `UsePostgreSqlStorage(connectionString, options)` มีจริงแต่เป็น
>    `[Obsolete]` ใน 1.21.1 และ repo ตั้ง `TreatWarningsAsErrors` → **ต้องใช้แบบ lambda**
>    `UsePostgreSqlStorage(pg => pg.UseNpgsqlConnection(connectionString), options)` (ตรงกับที่ร่างไว้รอบแรก)
> 2. **§7.1 Testcontainers** — `new PostgreSqlBuilder()` (parameterless) เป็น `[Obsolete]`
>    → **ต้องส่ง image ทาง constructor** `new PostgreSqlBuilder("postgres:17-alpine")` ไม่ใช่ `.WithImage(...)`
> 3. **§6 checklist "41 ตาราง"** — ตัวเลขนั้นมาจาก scaffold รอบ D-17 (2026-08-20) ซึ่งล้าสมัย
>    ของจริงคือ **65 ตาราง** ยืนยันด้วยการ diff ชุดชื่อตารางกับ migration เดิมใน git
>
> ### เปลี่ยนจากแผนโดยตั้งใจ
> - **§7.1 ไม่เปลี่ยนชื่อ `SqlConnectionString`** เป็น `PostgresConnectionString` — มี 35 ไฟล์อ่าน property นี้
>   การเปลี่ยนจะกลบ diff จริงของงานนี้ด้วย churn เชิงกล โดยไม่บอกอะไรที่ type ไม่ได้บอกอยู่แล้ว
> - **§5.2 promo code** — แปลงตรง ๆ ตามที่เขียนไว้ แต่พบว่า **ทั้ง `UPDLOCK, HOLDLOCK` เดิมและ `FOR UPDATE`
>   ใหม่ไม่เคยกัน TOCTOU ได้จริง** เพราะไม่มี transaction ครอบเลย → ไม่ใช่ regression ของงานนี้
>   บันทึกเป็น `X-26` ใน `docs/TASKS.md` และเขียนกำกับไว้ในโค้ด **ไม่แก้** (เปลี่ยนพฤติกรรมเรื่องเงิน)
> - **§8 `dr-backup-drill.sh` ไม่แตะ** — พบว่าเป็นสคริปต์ปลอมทั้งไฟล์ บันทึกเป็น `X-25`

> **นี่คือ contract ตาม `ANTIGRAVITY_HANDOFF.md` §2 ข้อ 10** — ชนะคำบรรยายในบล็อกคำสั่งอื่นถ้าขัดกัน
> ถ้าพบว่าทำตามไม่ได้จริง **ให้หยุดแล้วรายงาน ห้ามแก้ไฟล์นี้เอง ห้าม deviate เงียบ ๆ**

---

## 0. คำสั่งและขอบเขต

เจ้าของโปรเจ็คสั่งเปลี่ยน database engine จาก **SQL Server 2022 → PostgreSQL 17** (2026-09-05)
คำสั่งนี้ **ทับ** ตาราง Stack ใน `CLAUDE.md` และ `docs/DECISIONS.md` Q3 เดิม — ไม่ต้องถามซ้ำ

**ตัดสินใจแล้ว (ห้ามเปลี่ยนเอง):**

| ประเด็น | คำตอบ | ใครตัดสิน |
|--------|-------|----------|
| ย้ายข้อมูลเดิมจาก MSSQL ไหม | **ไม่ย้าย** — เริ่ม schema ใหม่จาก migration แล้วรัน `--seed` ใหม่ | เจ้าของโปรเจ็ค 2026-09-05 |
| PostgreSQL ติดตั้งแบบไหน | **Docker container** บน Contabo (ติดตั้งเสร็จแล้ว ดู §1) | เจ้าของโปรเจ็ค 2026-09-05 |
| Full-text search ภาษาไทย | **pg_trgm (trigram)** ไม่ใช่ `tsvector` — ดู §5.1 | Claude Code (เหตุผลใน §5.1) |
| Concurrency token | **คง `byte[]` + interceptor** ไม่ใช้ `xmin` — ดู §4.3 | Claude Code (เหตุผลใน §4.3) |

**นอกขอบเขตงานนี้ ห้ามแตะ:** business logic ใด ๆ, API contract ที่ frontend เห็น, `siri_upskill_ui` ทั้ง repo
เป้าหมายคือ **พฤติกรรมเดิมทุกอย่าง แค่เปลี่ยน engine** — ถ้าเจอบั๊ก logic ระหว่างทาง ให้บันทึกไว้ อย่าแก้ในงานนี้

---

## 1. สถานะ infra — Claude Code ทำเสร็จแล้ว ไม่ต้องทำซ้ำ

PostgreSQL 17 รันอยู่บน Contabo VPS แล้วจริง (verify แล้ว ไม่ใช่แค่สั่งไป):

| รายการ | ค่า |
|-------|-----|
| Container | `siri_postgres` (`postgres:17-alpine`, `--restart unless-stopped`) |
| Volume | `siri_pg_data` |
| Docker network | `siri-net` (สร้างใหม่) + publish `127.0.0.1:5432` เท่านั้น (**ไม่เปิดออกเน็ต** ต่างจาก MSSQL 1433 เดิม) |
| Database | `SIRIUPSKILL` (ตัวใหญ่ทั้งหมด — ดูหมายเหตุใต้ตาราง) · `ENCODING UTF8` · `LOCALE_PROVIDER icu` · `ICU_LOCALE 'th-TH'` |
| App role | `siriupskill_app` — เป็น **owner ของ database นี้เท่านั้น ไม่ใช่ superuser** (ดีกว่าเดิมที่ prod ใช้ `sa`) |
| Extension | `pg_trgm` สร้างได้จริงด้วย role นี้ (trusted extension, ยืนยันแล้ว) |
| รหัสผ่าน | อยู่ที่ `/root/.siri-postgres.env` บน VPS (`chmod 600`) — **ห้าม commit ลง repo เด็ดขาด** |
| tuning | `max_connections=200 shared_buffers=512MB effective_cache_size=1536MB shm_size=256m` |

**รูปแบบ connection string (Npgsql — ไม่ใช่ syntax เดิมของ SqlClient):**

```
# จาก container อื่นบน siri-net (production)
Host=siri_postgres;Port=5432;Database=SIRIUPSKILL;Username=siriupskill_app;Password=<จาก /root/.siri-postgres.env>

# จากเครื่อง dev ผ่าน SSH tunnel (ดูวิธีเปิด tunnel ข้างล่าง) — ใช้ port 15432 ฝั่ง local
Host=127.0.0.1;Port=15432;Database=SIRIUPSKILL;Username=siriupskill_app;Password=<...>;Include Error Detail=true
```

**ต่อจากเครื่อง dev ส่วนตัว — ผ่าน SSH tunnel (ทดสอบแล้วใช้ได้จริง 2026-09-05):**

```bash
ssh -f -N -o ExitOnForwardFailure=yes -o ServerAliveInterval=30 -L 15432:127.0.0.1:5432 root@217.217.253.122
```

- ใช้ port **15432** ฝั่ง local (เลี่ยงชนกับ PostgreSQL ที่อาจลงไว้ในเครื่องอยู่แล้วที่ 5432)
- `-f -N` = ทำงานเบื้องหลัง ไม่เปิด shell · `ServerAliveInterval=30` กันหลุดตอนไม่มีทราฟฟิก
- tunnel **หายเมื่อรีบูตหรือปิดเครื่อง** ต้องสั่งใหม่ — ปิดเองด้วย
  `taskkill //F //FI "IMAGENAME eq ssh.exe"` (Windows) หรือ `pkill -f "15432:127.0.0.1:5432"` (Linux/macOS)
- ใช้กับ GUI ได้ทุกตัว (DBeaver / pgAdmin / DataGrip): host `127.0.0.1` port `15432` db `SIRIUPSKILL` user `siriupskill_app`
- ตั้งค่าให้แอปที่รันบนเครื่อง dev: `dotnet user-secrets set "ConnectionStrings:Default" "<connection string ข้างบน>" --project src/Siri.Api`

> **ทำไมไม่เปิดพอร์ต 5432 ออกเน็ตตรง ๆ** (ถามแล้วตอบไว้ตรงนี้เพื่อไม่ให้มีคนมาเปิดทีหลังโดยไม่รู้เหตุผล):
> 1. `DEPLOYMENT.md` และ `.claude/rules/security.md` ห้ามไว้ตรง ๆ — และ 1433 ที่เปิดค้างอยู่ก็ถูกบันทึกเป็นหนี้ที่ต้องแก้
> 2. **`ufw allow` ใช้คุม container ไม่ได้จริง** — Docker แทรกกฎ iptables ของตัวเองก่อน ufw ดังนั้นถ้า publish
>    `0.0.0.0:5432` แล้วไปตั้ง `ufw allow from <ip> to any port 5432` **พอร์ตจะเปิดให้ทั้งอินเทอร์เน็ตอยู่ดี**
>    จะจำกัดจริงต้องเขียนกฎใน chain `DOCKER-USER` ไม่ใช่ ufw
> 3. IP บ้าน (`223.206.225.187` ตอนตรวจ) เป็น dynamic — allowlist จะพังเองเมื่อ ISP เปลี่ยน IP
>
> ถ้าเจ้าของโปรเจ็คยังต้องการเปิดตรงจริง ๆ ให้สั่งเป็นงานแยกและทำผ่าน `DOCKER-USER` chain เท่านั้น

> ⚠️ `Include Error Detail=true` ใส่ได้เฉพาะ dev — มันพ่นค่าคอลัมน์จริงออกมาใน error message ขัด `security.md` ถ้าใช้ที่ prod

**หมายเหตุเรื่องชื่อ database ตัวใหญ่** (เจ้าของโปรเจ็คสั่ง 2026-09-05 — เดิมสร้างเป็น `siriupskill` แล้ว
`ALTER DATABASE ... RENAME TO "SIRIUPSKILL"` ทีหลัง ยืนยันแล้วว่า owner / locale `th-TH` / `pg_trgm` อยู่ครบหลัง rename):

- **ใน connection string เขียน `Database=SIRIUPSKILL` ตรง ๆ ได้เลย ไม่ต้อง quote** — Npgsql ส่งชื่อ database
  เป็น startup parameter ของ protocol ไม่ใช่ SQL จึงไม่โดน identifier folding
- **ใน SQL / `psql -c` ต้อง quote เสมอ**: `ALTER DATABASE "SIRIUPSKILL" ...` — ถ้าไม่ quote PostgreSQL จะ fold
  เป็น `siriupskill` แล้วบอกว่าไม่มี database ชื่อนี้
- `psql -d SIRIUPSKILL` (ผ่าน flag) ใช้ได้ปกติ เพราะเป็น argument ไม่ใช่ SQL
- role ยังเป็น **`siriupskill_app` ตัวเล็กเหมือนเดิม** — เปลี่ยนเฉพาะชื่อ database ตามที่สั่ง

**MSSQL เดิมยังรันอยู่ ห้ามหยุดหรือลบ** จนกว่าเจ้าของโปรเจ็คจะยืนยันว่า PostgreSQL ใช้งานได้จริงครบ

---

## 2. สถานะโค้ด ณ ตอนรับงาน — repo **build ไม่ผ่าน** โดยตั้งใจ

Claude Code สลับ NuGet package ไปแล้ว (ผ่าน `dotnet add/remove package` จริง — **version เหล่านี้ resolve มาจริง ห้ามเดาใหม่เอง**):

| ออก | เข้า | version |
|-----|------|--------|
| `Microsoft.EntityFrameworkCore.SqlServer` | `Npgsql.EntityFrameworkCore.PostgreSQL` | **10.0.3** |
| `Hangfire.SqlServer` | `Hangfire.PostgreSql` | **1.21.1** |
| `Testcontainers.MsSql` | `Testcontainers.PostgreSql` | **4.14.0** |
| `Microsoft.Data.SqlClient` | `Npgsql` | **10.0.3** |

แปลว่าตอนนี้ `dotnet build` **แดง** เพราะโค้ดยังเรียก API ของ SQL Server อยู่
งานของคุณคือ §3–§9 เพื่อพากลับมาเขียว — **นี่คือจุดเริ่มที่คาดไว้ ไม่ใช่ความผิดพลาด**

> โมดูล 7 ตัวไม่ได้ reference provider package ตรง ๆ แต่ได้ transitively ผ่าน `Siri.Persistence`
> (คอมเมนต์ "Provider-agnostic EF Core only (no .SqlServer here)" ในแต่ละ `.csproj` พูดถึงเจตนา ไม่ใช่การบังคับทาง build)
> จึงเรียก extension method ของ Npgsql (`IncludeProperties`, `HasMethod`, `HasOperators`) ได้ทันทีโดยไม่ต้องเพิ่ม reference
> **อัปเดตข้อความคอมเมนต์เหล่านั้นให้เขียนว่า `.Npgsql` แทน `.SqlServer` ด้วย** (7 ไฟล์ `.csproj`)

---

## 3. Provider wiring

### 3.1 `src/Siri.Persistence/DependencyInjection/PersistenceServiceCollectionExtensions.cs:49`

```csharp
// เดิม
.UseSqlServer(connectionString, sql => sql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
// ใหม่
.UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
```

แก้ doc comment บรรทัด 12 ที่เขียนว่า "against SQL Server" ด้วย

### 3.2 `src/Siri.Workers/WorkersServiceCollectionExtensions.cs`

- `using Hangfire.SqlServer;` → `using Hangfire.PostgreSql;`
- `UseSqlServerStorage(connectionString, new SqlServerStorageOptions { SchemaName = "HANGFIRE", PrepareSchemaIfNecessary = true })`
  → `UsePostgreSqlStorage(connectionString, new PostgreSqlStorageOptions { SchemaName = "hangfire", PrepareSchemaIfNecessary = true })`

  > **overload นี้ยืนยันแล้วว่ามีจริงใน 1.21.1** — อ่านจาก `Hangfire.PostgreSql.xml` ในแพ็กเกจที่ restore มาแล้ว:
  > `UsePostgreSqlStorage(IGlobalConfiguration, System.String, PostgreSqlStorageOptions)`
  > (แบบ lambda `UsePostgreSqlStorage(c => c.UseNpgsqlConnection(...), options)` ก็มี แต่
  > `PostgreSqlBootstrapperOptions.UseNpgsqlConnection` รับ `(String, Action<NpgsqlConnection>)`
  > **ใช้แบบ connection string ตรง ๆ ง่ายกว่าและตรงกับของเดิมที่สุด**)
- **`SchemaName` ใช้ตัวพิมพ์เล็ก `"hangfire"`** ไม่ใช่ `"HANGFIRE"` — เป็น schema ของ Hangfire เอง ไม่ใช่ของแอป
  (ตาราง Hangfire สร้างโดย Hangfire เองไม่ผ่าน EF migration เหมือนเดิม) อัปเดตคอมเมนต์ที่อธิบายเรื่อง schema ให้ตรง
- `SetDataCompatibilityLevel(CompatibilityLevel.Version_180)` / `WithJobExpirationTimeout` **คงเดิม**

---

## 4. EF Core mapping ที่ผูกกับ SQL Server

### 4.1 Filtered index — `HasFilter` ใช้ syntax `[COL]` ของ SQL Server (5 จุด)

PostgreSQL ต้องใช้ **double quote** และ boolean literal (ทุก identifier ในระบบนี้เป็น UPPERCASE จาก
`ApplyUppercaseNamingConventions` → PostgreSQL fold เป็นตัวเล็กถ้าไม่ quote → พังทันที)

| ไฟล์:บรรทัด | เดิม | ใหม่ |
|---|---|---|
| `Siri.Modules.Catalog/Infrastructure/CourseConfiguration.cs:47` | `[IS_DELETED] = 0` | `"IS_DELETED" = false` |
| `Siri.Modules.Cms/Infrastructure/PostConfiguration.cs:27` | `[IS_DELETED] = 0` | `"IS_DELETED" = false` |
| `Siri.Modules.Commerce/Infrastructure/REFUNDConfiguration.cs:46` | `[STRIPE_REFUND_ID] IS NOT NULL` | `"STRIPE_REFUND_ID" IS NOT NULL` |
| `Siri.Modules.Identity/Infrastructure/UserSecurityTokenConfiguration.cs:42` | `[CONSUMED_AT_UTC] IS NULL` | `"CONSUMED_AT_UTC" IS NULL` |
| `Siri.Modules.Identity/Infrastructure/UserSessionConfiguration.cs:39` | `[REVOKED_AT_UTC] IS NULL` | `"REVOKED_AT_UTC" IS NULL` |

(ในโค้ด C# ต้อง escape เป็น `"\"IS_DELETED\" = false"` หรือใช้ raw string literal)

### 4.2 `HasColumnType`

- **`"nvarchar(max)"` → `"text"`** (5 จุดที่ยืนยันแล้ว — ไล่ `grep -rn 'nvarchar' src --include=*.cs` ให้ครบเผื่อมีเพิ่ม):
  `Commerce/STRIPE_WEBHOOK_EVENTConfiguration.cs:26` · `Identity/SecurityAuditConfiguration.cs:20` ·
  `Learning/QuizAttemptAnswerConfiguration.cs:32` · `Notification/AnnouncementConfiguration.cs:18` ·
  `Notification/EmailOutboxMessageConfiguration.cs:23`
- **`"char(3)"` / `"char(7)"` คงเดิม** — PostgreSQL มี `character(n)` จริง Npgsql map ให้ถูก
  (`Catalog/CourseConfiguration.cs:75` · `Commerce/ORDERConfiguration.cs:31` · `Payout/PayoutBatchConfiguration.cs:34` · `Payout/RevenueSplitConfiguration.cs:33`)
- `HasPrecision(18,2)` → `numeric(18,2)` อัตโนมัติ **ไม่ต้องแก้**
- `HasPrecision(3)` บน `DateTime` → `timestamptz(3)` **ไม่ต้องแก้**

### 4.3 `IsRowVersion()` — SQL Server เท่านั้น (4 จุด) → **concurrency token ที่ดูแลเองด้วย interceptor**

PostgreSQL ไม่มี `rowversion` ทางเลือกที่คนมักแนะนำคือ `UseXminAsConcurrencyToken()` แต่ **ห้ามใช้ในงานนี้ และใช้ไม่ได้จริงด้วย**

> **ตรวจแล้ว: `Npgsql.EntityFrameworkCore.PostgreSQL 10.0.3` ไม่มี API ตัวนี้** — grep `xmin` (ไม่สนตัวพิมพ์)
> ใน `Npgsql.EntityFrameworkCore.PostgreSQL.xml` ของแพ็กเกจที่ restore มาแล้ว = 0 hit
> ถ้าไปเจอตัวอย่างในอินเทอร์เน็ตที่ใช้ `UseXminAsConcurrencyToken()` **อย่าเสียเวลาลอง** มันเป็นของเวอร์ชันเก่ากว่านี้

และต่อให้มี ก็ยังห้ามใช้อยู่ดี เพราะ `xmin` เป็น `uint` ซึ่งจะเปลี่ยน API contract: `GetCourseBuilder`/`AutosaveCourse` ส่ง `rowVersion`
เป็น **base64 string** และ `siri_upskill_ui` ผูกไว้เป็น `readonly rowVersion: string` แล้ว
(`features/catalog/data/course-api.models.ts` 3 จุด + `course-builder-page.ts`) — งานนี้ห้ามแตะ frontend

**วิธีที่ต้องทำ:**

1. ทั้ง 4 จุดเปลี่ยน `.IsRowVersion()` → `.IsConcurrencyToken().HasColumnType("bytea").IsRequired()`
   - `Catalog/CourseConfiguration.cs:98` (`RowVersion`)
   - `Commerce/ORDERConfiguration.cs:47` (`ROW_VERSION`)
   - `Commerce/PROMO_CODEConfiguration.cs:40` (`ROW_VERSION`)
   - `Learning/EnrollmentConfiguration.cs:55` (`ROW_VERSION`)
   - **ห้ามใส่ `ValueGeneratedOnAddOrUpdate()`** — ไม่มีอะไรใน PostgreSQL มา generate ให้
2. สร้าง `src/Siri.Persistence/Interceptors/ConcurrencyTokenInterceptor.cs` (`SaveChangesInterceptor`)
   - ใน `SavingChanges`/`SavingChangesAsync`: ทุก entry ที่ `State` เป็น `Added` หรือ `Modified`
     และมี property ที่ `IsConcurrencyToken` และเป็น `byte[]` → ตั้ง
     `entry.Property(name).CurrentValue = RandomNumberGenerator.GetBytes(8)`
   - **ห้ามแตะ `OriginalValue`** — EF ใช้ค่านั้นเป็น `WHERE` ของ `UPDATE` (คือหัวใจของการเช็ค conflict)
     `AutosaveCourse/Handler.cs:59` ตั้ง `OriginalValue` จากค่าที่ client ส่งมา — ต้องยังทำงานเหมือนเดิมเป๊ะ
   - เขียนค่าผ่าน `entry.Property(...)` (ชื่อ) ไม่ใช่ property setter — domain property เป็น `private set`
3. ลงทะเบียนต่อจาก `AuditableEntityInterceptor` ใน `PersistenceServiceCollectionExtensions`
   (`services.AddScoped<ConcurrencyTokenInterceptor>()` + `.AddInterceptors(...)` ทั้งสองตัว)
4. **unit test บังคับ** (`Siri.UnitTests`): ยืนยันว่า insert ตั้งค่า token, update เปลี่ยนค่า token,
   และ entity ที่ไม่มี concurrency token ไม่ถูกแตะ
5. **integration test บังคับ**: `CourseBuilderTests.AutosaveCourse_StaleRowVersion_Returns409Conflict`
   ต้องยังผ่าน — เป็นหลักฐานว่า optimistic concurrency ยังทำงานจริงบน PostgreSQL

### 4.4 `IncludeProperties` (3 จุด) — **ไม่ต้องแก้**

PostgreSQL 11+ รองรับ `INCLUDE` และ Npgsql มี extension method ชื่อเดียวกัน แค่ยืนยันว่า compile ผ่าน
(`Catalog/CourseConfiguration.cs:108` · `Learning/EnrollmentConfiguration.cs:66` · `Learning/EpisodeProgressConfiguration.cs:60`)

### 4.5 identifier ยาวเกิน 63 ไบต์ — เพิ่ม **guard** ไม่ใช่ truncate

PostgreSQL ตัด identifier ที่ยาวเกิน 63 ไบต์ **เงียบ ๆ** (SQL Server ยอมถึง 128) → ชื่อชนกันได้โดยไม่มีใครรู้
ตอนนี้ชื่อยาวสุดคือ 53 ตัวอักษร (`FK_REFRESH_TOKENS_REFRESH_TOKENS_REPLACED_BY_TOKEN_ID`) — **ยังปลอดภัย**

เพิ่มเทสต์ใน `Siri.ArchitectureTests` (หรือ `Siri.UnitTests` ถ้าเข้าถึง model ได้ง่ายกว่า): สร้าง model
จาก `AppDbContext` แล้ว assert ว่าทุก schema / table / column / key / FK / index name ยาว ≤ 63 ไบต์ (UTF-8)
ข้อความ fail ต้องบอกชื่อที่ยาวเกินตรง ๆ **ห้ามแก้ปัญหาด้วยการ truncate อัตโนมัติ** — จะกลายเป็นชื่อที่อ่านไม่รู้เรื่อง

---

## 5. Raw SQL — 4 จุด (ทั้งหมดเป็น syntax SQL Server)

### 5.1 `Siri.Modules.Catalog/Features/SearchCourses/Handler.cs:38-80` — **เขียนใหม่ทั้งบล็อก**

**ทำไมไม่ใช้ `tsvector` ของ PostgreSQL:** parser ของ PostgreSQL FTS ตัดคำด้วยช่องว่าง — ภาษาไทยไม่มีช่องว่างระหว่างคำ
`to_tsvector('simple', 'ลดหย่อนภาษีอย่างถูกวิธี')` จะได้ token เดียวทั้งประโยค ค้นหาไม่เจออะไรเลย
**pg_trgm ทำงานที่ระดับตัวอักษร 3 ตัว** จึงใช้กับภาษาไทยได้จริงและให้คะแนนความใกล้เคียงมาด้วย

**สิ่งที่ต้องทำ:**

1. ใน `CourseConfiguration`:
   ```csharp
   builder.HasIndex(c => c.Title).HasMethod("gin").HasOperators("gin_trgm_ops");
   builder.HasIndex(c => c.Subtitle).HasMethod("gin").HasOperators("gin_trgm_ops");
   builder.HasIndex(c => c.Description).HasMethod("gin").HasOperators("gin_trgm_ops");
   ```
2. ใน `AppDbContext.OnModelCreating`: `modelBuilder.HasPostgresExtension("pg_trgm");`
   (Npgsql จะออก `CREATE EXTENSION IF NOT EXISTS` ให้ใน migration — `siriupskill_app` มีสิทธิ์พอ ยืนยันแล้วบน VPS จริง)

   > API ทั้งสี่ตัวที่ contract นี้อ้าง **ยืนยันจาก XML doc ของ `Npgsql.EntityFrameworkCore.PostgreSQL 10.0.3`
   > ที่ restore มาแล้ว ไม่ใช่เดา**: `HasPostgresExtension(ModelBuilder, String)` ✓ · `IncludeProperties` ✓ ·
   > `HasMethod` ✓ · `HasOperators` ✓ (ทั้งหมดอยู่ใน namespace `Microsoft.EntityFrameworkCore` จึงไม่ต้องเพิ่ม `using`)
3. แทน `FREETEXTTABLE` ด้วยแบบนี้ (ทุก identifier **และ alias** ต้อง quote ไม่งั้น `SqlQuery<T>` map ไม่ตรง
   เพราะ PostgreSQL fold alias ที่ไม่ quote เป็นตัวพิมพ์เล็ก):
   ```csharp
   var pattern = $"%{queryText}%";
   var matches = await dbContext.Database
       .SqlQuery<CourseSearchMatch>($"""
           SELECT "ID" AS "CourseId",
                  (similarity("TITLE", {queryText}) * 100
                   + similarity(COALESCE("SUBTITLE", ''), {queryText}) * 50
                   + similarity(COALESCE("DESCRIPTION", ''), {queryText}) * 10)::int AS "Rank"
           FROM "CATALOG"."COURSES"
           WHERE "IS_DELETED" = false
             AND "STATUS" = 'Published'
             AND ("TITLE" ILIKE {pattern}
                  OR "SUBTITLE" ILIKE {pattern}
                  OR "DESCRIPTION" ILIKE {pattern})
           """)
       .ToListAsync(cancellationToken).ConfigureAwait(false);
   ```
   (`STATUS` เก็บเป็น string ผ่าน `HasConversion<string>()` จึงเทียบกับ `'Published'` ตรง ๆ ได้)
4. **ลบ `try`/`catch` + LIKE fallback ทั้งบล็อก (บรรทัด ~54-79) ทิ้ง** — fallback นั้นมีไว้เพราะ MSSQL บน Contabo
   ไม่ได้ติดตั้ง Full-Text Search component (error 7609) `pg_trgm` มาจาก migration ของเราเองจึงมีแน่นอน 100%
   **ปิด `X-7` และ `P0-40` ไปพร้อมกัน** — อัปเดต `docs/TASKS.md` ทั้งสองแถวให้สะท้อนว่าไม่เกี่ยวข้องแล้ว
5. `ILIKE` ให้พฤติกรรม case-insensitive เหมือนที่ collation `Thai_100_CI_AS` เคยให้ฟรี — **ห้ามใช้ `LIKE` เฉย ๆ**
6. `SearchCoursesTests.cs` มีคอมเมนต์อธิบายความเสี่ยงเรื่อง FTS ที่ไม่มีใน test container — **ลบทิ้ง** ไม่จริงแล้ว

### 5.2 `Siri.Modules.Commerce/Infrastructure/PromoCodeRepository.cs:27`

```csharp
// เดิม — locking hint ของ SQL Server
$"SELECT * FROM COMMERCE.PROMO_REDEMPTIONS WITH (UPDLOCK, HOLDLOCK) WHERE PROMO_CODE_ID = {promoCodeId} AND USER_ID = {userId}"
// ใหม่ — PostgreSQL row lock
$"""SELECT * FROM "COMMERCE"."PROMO_REDEMPTIONS" WHERE "PROMO_CODE_ID" = {promoCodeId} AND "USER_ID" = {userId} FOR UPDATE"""
```

⚠️ **ความหมายไม่เท่ากันเป๊ะ:** `UPDLOCK, HOLDLOCK` ล็อกช่วง key (กันการ **insert** แถวใหม่เข้ามาในช่วงนั้น)
ส่วน `FOR UPDATE` ล็อกเฉพาะแถวที่มีอยู่แล้ว — สองคนกดโค้ดส่วนลดเดียวกันพร้อมกันครั้งแรกจะไม่ถูกกั้น
**ต้องปิดช่องนี้ด้วย unique constraint ที่ระดับ DB** ไม่ใช่การล็อก:

- ยืนยันว่า `PROMO_REDEMPTIONS` มี unique index บน `(PROMO_CODE_ID, USER_ID, ORDER_ID)` อยู่แล้วหรือไม่ ถ้าไม่มีให้เพิ่ม
- ถ้า `maxPerUser` > 1 (unique ไม่พอ) → **หยุดแล้วรายงาน** อย่าเดาวิธีแก้เอง (แตะเรื่องเงิน = ขอบเขตฝั่ง Claude ตาม §2 ของ handoff)
- เขียน integration test ที่ยิง redeem พร้อมกัน 2 request ด้วย user เดียวกัน แล้วยืนยันว่าสำเร็จแค่ครั้งเดียว

### 5.3 `Siri.Modules.Media/Infrastructure/Seeding/MediaSeeder.cs:69`

quote identifier ให้ครบ:
`UPDATE "CATALOG"."COURSE_EPISODES" SET "MEDIA_ASSET_ID" = {assetId} WHERE "MEDIA_ASSET_ID" IS NULL OR "MEDIA_ASSET_ID" NOT IN (SELECT "MEDIA_ASSET_ID" FROM "MEDIA"."MEDIA_ASSETS")`

### 5.4 `tests/Siri.IntegrationTests/LearningIntegrationTests.cs:432`

`UPDATE "LEARNING"."WATCH_EVENTS" SET "OCCURRED_AT_UTC" = {occurredAtUtc} WHERE "WATCH_EVENT_ID" = {watchEventId}`

> **กฎรวมสำหรับ raw SQL ทุกจุดตั้งแต่นี้ไป:** identifier ทุกตัว (schema/table/column/alias) ต้องอยู่ใน double quote
> เพราะทั้งระบบใช้ชื่อ UPPERCASE ซึ่ง PostgreSQL จะ fold เป็นตัวเล็กถ้าไม่ quote
> เพิ่มกฎข้อนี้ลง `.claude/rules/database.md` ด้วย

---

## 6. Migration — ลบของเดิมทั้งหมด สร้างใหม่

migration 3 ตัวปัจจุบันเป็น SQL Server ล้วน (`SqlServer:Identity`, `SqlServer:Include`, `rowversion`)
และเจ้าของโปรเจ็คตัดสินแล้วว่า **ไม่ย้ายข้อมูลเดิม** → สร้างใหม่หมดได้

1. ลบทั้ง 7 ไฟล์ใน `src/Siri.Persistence/Migrations/` (3 migration + 3 Designer + `AppDbContextModelSnapshot.cs`)
2. `dotnet ef migrations add InitialCreatePostgres --project src/Siri.Persistence --startup-project src/Siri.Api`
3. **อ่าน migration ที่ generate ออกมาทั้งไฟล์ก่อนใช้** (`database.md` บังคับ) แล้วยืนยันเป็นข้อ ๆ ในรายงาน:
   - [ ] มี `AlterDatabase().Annotation("Npgsql:PostgresExtension:pg_trgm", ...)` จริง
   - [ ] มี `CreateTable` ครบ **41 ตาราง** เท่าของเดิม ไม่ขาดไม่เกิน
   - [ ] ไม่มี shadow column / shadow FK โผล่มาเพิ่ม (เคยเกิดจริงตอน P1-02 กับ `Course.Sections`)
   - [ ] `onDelete` เป็น `NoAction`/`Restrict` บน **ทุก** ตารางเงินและสิทธิ์เรียน
         (`ORDERS`, `PAYMENTS`, `REFUNDS`, `REVENUE_SPLITS`, `ENROLLMENTS`, `CERTIFICATES`)
         ยกเว้น `ORDER_ITEMS`/`EPISODE_PROGRESS` ที่เป็น `Cascade` โดยตั้งใจ (composition child)
   - [ ] `ROW_VERSION` ทั้ง 4 ตารางเป็น `bytea` ไม่ใช่ `rowversion`
   - [ ] `WATCH_EVENTS.WATCH_EVENT_ID` เป็น identity column ปกติ
   - [ ] filtered index 5 ตัวออกมาเป็น `CREATE UNIQUE INDEX ... WHERE ...` ที่ syntax ถูก
   - [ ] GIN trigram index 3 ตัวบน `COURSES` มีจริง
4. **ห้ามรัน `dotnet ef database update` ใส่ Contabo เอง** (`ANTIGRAVITY_HANDOFF.md` §2 ข้อ 5)
   ทดสอบด้วย PostgreSQL ใน Docker ของตัวเองหรือปล่อยให้ integration test พิสูจน์
5. `scripts/migrate-bundle.sh` — อ่านทั้งไฟล์ ถ้ามีจุดไหนพูดถึง SQL Server ให้แก้คอมเมนต์ให้ตรง
   (ตัว `dotnet ef migrations bundle` เองไม่ผูกกับ provider จึงไม่น่าต้องแก้ logic)

---

## 7. เทสต์

### 7.1 `tests/Siri.IntegrationTests/Fixtures/ContainersFixture.cs`

```csharp
using Testcontainers.PostgreSql;   // แทน Testcontainers.MsSql

private readonly PostgreSqlContainer _sqlContainer =
    new PostgreSqlBuilder("postgres:17-alpine").Build();
```

> **ยืนยันแล้วว่า ctor รับ string มีจริงใน 4.14.0** (`PostgreSqlBuilder.#ctor(System.String)` ใน XML doc
> ของแพ็กเกจ) — รูปแบบเดียวกับ `new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest")` เดิมเป๊ะ
> จึงเป็น diff ที่เล็กที่สุด · `.WithImage(...)` ก็ใช้ได้ แต่ไม่ตรงกับสไตล์ที่ไฟล์นี้เขียนอยู่

- `SqlConnectionString` → เปลี่ยนชื่อเป็น `PostgresConnectionString` (ไล่แก้ทุก caller — `SiriApiFactory.cs:68,86,89`)
  หรือคงชื่อเดิมไว้ถ้ากลัวกระทบเยอะ **แต่ต้องเลือกอย่างใดอย่างหนึ่งแล้วทำให้ครบ**
- แก้ doc comment ที่เขียนว่า "MSSQL container" และอ้าง `database.md` ให้ตรงความจริงใหม่
- `PostgreSqlBuilder` ให้ database `postgres` + user `postgres` (superuser) → `CREATE EXTENSION pg_trgm` ใน migration ผ่านแน่นอน
- test container **ไม่มี** ICU locale `th-TH` เหมือน production — กระทบแค่ลำดับ `ORDER BY` ของข้อความไทย
  ถ้ามีเทสต์ไหน assert ลำดับการเรียงข้อความไทย ให้บันทึกไว้ อย่าเปลี่ยน assertion เพื่อให้ผ่าน

### 7.2 `tests/Siri.IntegrationTests/ContainersSmokeTests.cs`

`using Microsoft.Data.SqlClient` → `using Npgsql`, `SqlConnection` → `NpgsqlConnection`,
`Containers_SqlServerAndRedis_AreReachable` → `Containers_PostgresAndRedis_AreReachable`

### 7.3 ที่ต้องเขียนเพิ่ม (บังคับ)

- unit test ของ `ConcurrencyTokenInterceptor` (§4.3 ข้อ 4)
- test identifier ≤ 63 ไบต์ (§4.5)
- integration test การค้นหาภาษาไทยผ่าน `pg_trgm` — ต้องพิสูจน์ว่า**ค้นด้วยคำไทยบางส่วนแล้วเจอจริง**
  (ของเดิมพิสูจน์ไม่ได้เลยเพราะ MSSQL FTS ไม่เคยติดตั้ง) และ**เรียงตาม rank ถูกต้อง**
- integration test promo code แข่งกัน (§5.2)

### 7.4 ที่ต้องยังผ่านเหมือนเดิม

`Siri.UnitTests` ทั้งหมด · `Siri.ArchitectureTests` 4/4 · `CourseBuilderTests` ทุกตัวโดยเฉพาะ `AutosaveCourse_StaleRowVersion_Returns409Conflict`

### 7.5 จุดที่ต้องเฝ้าเป็นพิเศษ — `DateTime.Kind`

Npgsql map `DateTime` เป็น `timestamp with time zone` และ **โยน exception ถ้า `Kind != Utc`** ตอนเขียน
(SQL Server ไม่สนใจ Kind เลย จึงไม่เคยเป็นปัญหามาก่อน)
ตรวจแล้วว่าทางหลักปลอดภัย: `SystemClock.UtcNow` คืน `DateTime.UtcNow` (Kind=Utc) และ
`LearningAnalyticsContract`/`CommerceStatsContract` ใช้ `date.ToDateTime(..., DateTimeKind.Utc)` ถูกต้องแล้ว
**แต่ถ้า integration test เจอ `InvalidCastException`/`ArgumentException` เรื่อง timestamp ให้ตามหาจุดที่สร้าง
`DateTime` โดยไม่ระบุ Kind แล้วแก้ที่ต้นทาง — ห้ามแก้ด้วยการเปลี่ยนคอลัมน์เป็น `timestamp without time zone`**

---

## 8. Config / deployment

| ไฟล์ | ทำอะไร |
|-----|--------|
| `src/Siri.Api/appsettings.json` + `.Development.json` | `ConnectionStrings:Default` เป็น `""` อยู่แล้ว **ไม่ต้องแก้ค่า** แต่แก้ `_comment` ถ้าอ้าง SQL Server |
| `src/Siri.Workers/appsettings.json` + `.Development.json` | เหมือนกัน |
| `docker-compose.prod.yml` (มี 2 ไฟล์: root ของ repo backend และ root ของโฟลเดอร์แม่ `C:\ProjectSiriUpSkill` — **แก้ทั้งคู่**) | service `mssql` → `postgres` (`postgres:17-alpine`, volume `pg_data`, healthcheck `pg_isready`), แก้ `depends_on` ของ `api`/`workers`, ลบ `MSSQL_SA_PASSWORD` |
| `.env.example` | เปลี่ยน key และตัวอย่าง connection string เป็นรูปแบบ Npgsql (§1) — **ไฟล์นี้ commit ได้เพราะมีแต่ placeholder** |
| `.env` / `.env.production` / `.env_prd` | **ห้ามแตะ** — gitignored และมีค่าจริง เจ้าของโปรเจ็คแก้เอง (§10) |
| `.gitlab-ci.yml` | job `backend` ใช้ `docker:27-dind` อยู่แล้ว Testcontainers จะดึง `postgres:17-alpine` เอง — **ไม่ต้องแก้** (อ่านทั้งไฟล์ยืนยันแล้ว) |
| `scripts/generate-production-secrets.sh` | **ต้องแก้ 2 บรรทัดที่ยืนยันแล้ว** — บรรทัด 27 `MSSQL_SA_PASSWORD=$DB_PASS` และบรรทัด 28 `ConnectionStrings__Default=Server=mssql,1433;Database=SIRIUPSKILL;User Id=sa;...` → เปลี่ยนเป็น `POSTGRES_PASSWORD` + `Host=siri_postgres;Port=5432;Database=SIRIUPSKILL;Username=siriupskill_app;Password=$DB_PASS` (ทิ้ง `TrustServerCertificate`/`MultipleActiveResultSets` ซึ่งเป็นของ SqlClient ล้วน · `Max Pool Size=200` Npgsql รองรับ เขียนเป็น `Maximum Pool Size=200`) · เช็ค `.ps1` คู่กันด้วย |
| `scripts/dr-backup-drill.sh` | **อย่าเสียเวลาแปลงเป็น `pg_dump` — ไฟล์นี้เป็นของปลอมทั้งไฟล์ ดู `X-25`** ไม่เคยต่อ DB เลยสักบรรทัด · แก้ในงานนี้เท่าที่ทำให้ไม่โกหกเรื่อง engine ก็พอ (หรือปล่อยไว้แล้วรายงาน) การเขียน DR drill จริงเป็นงานแยก ไม่ใช่ขอบเขต P0-41 |
| `.env.example` | ⚠️ Claude Code อ่านไฟล์นี้ไม่ได้ (โดน deny rule ของ `.env*`) — **Antigravity ต้องเปิดอ่านเองแล้วเปลี่ยน key ที่เกี่ยวกับ MSSQL ทั้งหมด** อ้างอิงรูปแบบจาก `generate-production-secrets.sh` แถวข้างบนได้ |

---

## 9. เอกสารที่ต้องอัปเดต (ส่วนหนึ่งของ Definition of Done)

- `CLAUDE.md` — ตาราง Stack (`SQL Server 2022+` → `PostgreSQL 17`), บล็อก "Database — ต่อจริงแล้ว", คำสั่งที่ใช้บ่อย
- `docs/DATABASE.md` — ชนิดข้อมูล (`nvarchar`→`text`/`varchar`, `datetime2`→`timestamptz`, `uniqueidentifier`→`uuid`, `rowversion`→`bytea`+interceptor), กฎ quote identifier
- `docs/DEPLOYMENT.md` — ทั้ง section MSSQL, backup/restore เป็น `pg_dump`, **และเรื่อง "SQL Server edition สำหรับ production" ที่ค้างอยู่ถือว่าตกไป** (PostgreSQL ไม่มีข้อจำกัด license แบบนั้น)
- `docs/ARCHITECTURE.md` — จุดที่อ้าง SQL Server
- `docs/DECISIONS.md` — เพิ่ม **D-20** บันทึกการเปลี่ยน engine พร้อมเหตุผลและวันที่ + มาร์คว่าทับ Q3 เดิมบางส่วน
- `.claude/rules/database.md` — provider ใหม่, กฎ quote identifier ใน raw SQL, กฎ 63 ไบต์, `IsRowVersion` ห้ามใช้แล้ว
- `.claude/rules/backend.md` — จุดที่อ้าง SQL Server
- `docs/TASKS.md` — เพิ่มแถว `P0-41` (งานนี้), ปิด `P0-40` และ `X-7` (FTS blocker ตกไป), ทบทวน `P0-09`

---

## 10. สิ่งที่ **ห้าม** Antigravity ทำ — เจ้าของโปรเจ็คทำเอง

1. `dotnet ef database update` / รัน migration bundle ใส่ Contabo
2. แก้ `.env` / `.env.production` / `.env_prd` (มีค่าจริง)
3. อ่านหรือ copy รหัสผ่านจาก `/root/.siri-postgres.env` ลงไฟล์ใด ๆ ใน repo
4. หยุด/ลบ container `siri_upskill_backend` เดิม หรือ `mssql-server.service`
5. `git push` / สร้าง MR

---

## 11. ลำดับที่แนะนำ (แต่ละข้อ build ให้ผ่านก่อนไปข้อถัดไป)

1. §3 provider wiring → build ยังแดงแต่ error น้อยลง
2. §4.1–4.2 mapping ตรงไปตรงมา
3. §4.3 concurrency token + interceptor + unit test → **build เขียวครั้งแรกตรงนี้**
4. §5 raw SQL ทั้ง 4 จุด
5. §6 regenerate migration + อ่านตรวจตาม checklist
6. §7 เทสต์ทั้งหมด
7. §4.5 guard 63 ไบต์
8. §8 config
9. §9 เอกสาร

**1 task = 1 commit ตาม `ANTIGRAVITY_HANDOFF.md` §2 ข้อ 2** — งานนี้ใหญ่พอที่จะแตกเป็นหลาย commit ได้
ใช้ prefix เดียวกันหมด: `chore(persistence): swap EF provider to Npgsql [P0-41]`,
`feat(catalog): trigram-based Thai course search [P0-41]` ฯลฯ

---

## 12. รายงานตอนจบต้องมี

- output จริงของ `dotnet build` (ต้อง 0 warning / 0 error — repo ตั้ง `TreatWarningsAsErrors`)
- output จริงของ unit + architecture test
- integration test: บอกตรง ๆ ว่ารันได้จริงไหม (เครื่อง dev ไม่มี Docker → `DockerUnavailableException` เป็นเรื่องปกติ
  แต่ต้องบอกว่า **ยังไม่เคยรันจริง** ห้ามสรุปว่าผ่าน)
- checklist §6 ข้อ 3 ทีละข้อพร้อมหลักฐาน
- อะไรที่ยังไม่ได้ทำและเพราะอะไร
