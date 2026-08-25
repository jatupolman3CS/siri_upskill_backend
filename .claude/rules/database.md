# Database Rules — MSSQL + EF Core 10

โครงสร้างตารางทั้งหมดอยู่ที่ `docs/DATABASE.md` — อ่านก่อนเพิ่ม/แก้ตาราง

## Migration

- เปลี่ยน schema ผ่าน **EF migration เท่านั้น** ห้ามแก้ DB ด้วยมือแล้วค่อยตามเขียนโค้ด
- **ห้ามแก้ไข migration ที่ apply ขึ้น shared env ไปแล้ว** — สร้างตัวใหม่มาแก้แทน
- ตั้งชื่อสื่อความหมาย: `AddCourseSeoFields` ไม่ใช่ `Update1`
- อ่าน migration ที่ generate ออกมาทุกครั้งก่อนใช้ — โดยเฉพาะ drop column / alter type / rename (EF ชอบสร้างเป็น drop+create ซึ่งทำข้อมูลหาย)
- Migration ที่มีความเสี่ยงต่อข้อมูล ต้องเขียน data migration script คู่กัน และทดสอบบน restore ของ staging ก่อน
- ขึ้น production ด้วย **migration bundle** เท่านั้น — ห้ามใส่ `Database.Migrate()` ใน `Program.cs`

## Schema convention

- PK = `uniqueidentifier` ค่า **UUIDv7** (`Guid.CreateVersion7()` ใน .NET 9+) ห้าม `NEWID()` (ทำ index กระจาย)
- เวลาเป็น `datetime2(3)` **UTC** ชื่อลงท้าย `AtUtc` — อ่านเวลาผ่าน `IClock` เท่านั้น ห้าม `DateTime.Now`/`UtcNow` ตรง ๆ (เทสต์ไม่ได้)
- เงิน `decimal(18,2)` — ระบุ `.HasPrecision(18, 2)` ใน configuration เสมอ ไม่งั้น EF จะ map เป็น `decimal(18,2)` เงียบ ๆ ที่อาจไม่ตรงเจตนา
- ข้อความใช้ `nvarchar` เสมอ + กำหนด `HasMaxLength()` ทุกคอลัมน์ (ไม่งั้นได้ `nvarchar(max)` ซึ่ง index ไม่ได้)
- Enum เก็บเป็น `string` (อ่าน DB รู้เรื่อง + ไม่พังเมื่อเรียงลำดับ enum ใหม่) หรือ smallint พร้อม lookup table — เลือกอย่างใดอย่างหนึ่งแล้วทำเหมือนกันทั้งระบบ
- Configuration แยกไฟล์ `IEntityTypeConfiguration<T>` ต่อ entity ห้ามใช้ data annotation ปนกับ fluent API

## ชื่อ entity/DB แบบ UPPERCASE (ข้อยกเว้นเฉพาะ 7 โมดูลใหม่)

ตัดสินใจ 2026-08-20 (`docs/DECISIONS.md` D-17) — ใช้กับ **Commerce, Media, Learning, Payout, Cms, Community, Analytics เท่านั้น**. Identity/Catalog/Notification/SharedKernel/Persistence ยังเป็น PascalCase ปกติทั้งหมด ห้ามแก้ย้อนหลัง

- DB schema/table name: UPPERCASE, table เป็นพหูพจน์เสมอ (กัน T-SQL reserved word เช่น `ORDER`/`USER`/`GROUP` — ใช้ `ORDERS`/`USERS`/`GROUPS` แทน) เช่น schema `COMMERCE`, table `ORDERS`
- DB column name: `UPPER_SNAKE_CASE` เช่น `ORDER_ID`, `TOTAL_AMOUNT`
- C# entity class: UPPERCASE เอกพจน์ (`ORDER`), property: `UPPER_SNAKE_CASE` 1:1 กับ column — แปลงด้วยกฎกลไก `Regex.Replace(name, "(?<=[a-z0-9])(?=[A-Z])", "_").ToUpperInvariant()` (เช่น `SubtotalAmount`→`SUBTOTAL_AMOUNT`) ยกเว้น PK ที่เป็นแค่ `Id` เปล่า ๆ ให้ใส่ชื่อ entity นำหน้าเอง (`ORDER_ID` ไม่ใช่ `ID`)
- **ข้อยกเว้นบังคับ ห้ามลืม**: property ที่มาจาก `IAuditable`/`ISoftDelete` (`CreatedAtUtc`/`CreatedBy`/`UpdatedAtUtc`/`UpdatedBy`/`IsDeleted`/`DeletedAtUtc`) **ต้องเป็น PascalCase ปกติใน C# เสมอ แม้ entity ที่เหลือจะเป็น UPPERCASE** — เปลี่ยนชื่อแล้วพัง: `AuditableEntityInterceptor` เขียนค่าผ่าน `entry.Property(nameof(IAuditable.CreatedAtUtc)).CurrentValue = ...` ซึ่งเป็นการค้นหาด้วยชื่อ C# property ตรง ๆ (ไม่ใช่ชื่อ column) ถ้าเปลี่ยนชื่อ property จะ throw `InvalidOperationException` ตอน `SaveChangesAsync` ครั้งแรกที่มี entity ติด track — **runtime, ไม่ใช่ compile time หรือ migration time** ส่วน column ของมันยังตั้งเป็น UPPERCASE ได้ปกติผ่าน `.HasColumnName(...)` ตรง ๆ
- Enum **type และ member เป็น PascalCase ปกติ** เหมือนเดิมทุกที่ — uppercase เฉพาะ **property ที่ถือ enum** เท่านั้น (เช่น `public OrderStatus STATUS { get; }`) เพราะ enum member โผล่ตรงใน JSON response ผ่าน `JsonStringEnumConverter()` อยู่แล้ว ไม่ควรเปลี่ยนโดยไม่มีเหตุผลเรื่อง contract
- Repository/Service class name, method name, DTO/Request/Response record, namespace, interface: **ไม่แตะ** ยังเป็น PascalCase/camelCase มาตรฐานทั้งหมด (เช่น `IOrderRepository` ไม่ใช่ `IORDERRepository`) — UPPERCASE จำกัดเฉพาะ entity class ที่ map ตรงกับ table เท่านั้น
- Navigation property ระหว่าง UPPERCASE entity ต้องระบุ `.HasForeignKey()`/`.WithMany(nav)` ชัดเจนเสมอ ห้ามพึ่ง convention discovery (เคยเจอ shadow FK จริงมาแล้วครั้งหนึ่งกับ `Course.Sections` ตอน PascalCase ปกติ — underscore ยิ่งทำให้ convention เดามั่วมากกว่าเดิม)
- ไม่ต้องแก้ `.editorconfig`/analyzer เพื่อให้ build ผ่าน (repo นี้ไม่ได้เปิด `CA1707`/`IDE1006` อยู่แล้ว) แต่ห้ามประกาศ stub method (`throw new NotImplementedException();` อย่างเดียว) เป็น `async` — ไม่มี `await` จริงจะโดน `CS1998` ซึ่งเป็น warning จริงและ build ตั้ง `TreatWarningsAsErrors=true`

## Query

- Query อ่านอย่างเดียวต้อง `.AsNoTracking()`
- **ห้าม N+1**: ใช้ `Include`/`ThenInclude` หรือ projection `.Select()` ให้ตรงกับที่ใช้จริง
- Projection ไปเป็น DTO ตรง ๆ ดีกว่าดึง entity มาทั้งก้อนแล้ว map
- รายการที่โตได้ต้อง paginate เสมอ (keyset pagination สำหรับ feed, offset ได้สำหรับ admin table)
- ห้าม string concat ใน raw SQL — ถ้าจำเป็นใช้ `FromSqlInterpolated`
- เขียน query ใหม่ที่แตะตารางใหญ่ (`WatchEvents`, `Courses`, `Orders`) ต้องบอกได้ว่าใช้ index ตัวไหน ถ้าไม่มีให้เพิ่ม index มาใน migration เดียวกัน

## Transaction & concurrency

- การเปลี่ยนแปลงหลายตารางที่ต้อง atomic (จ่ายเงิน → สร้าง enrollment → บันทึก revenue split) ต้องอยู่ใน transaction เดียว หรือใช้ outbox
- ตารางที่แก้พร้อมกันได้ต้องมี `rowversion` และจัดการ `DbUpdateConcurrencyException` ให้ชัด
- นับโควตา (promo code, ที่นั่ง) ต้อง atomic ที่ระดับ SQL ห้ามอ่านมาเช็คใน memory แล้วค่อยเขียน

## ข้อห้าม

- ❌ Cascade delete บน Orders / Payments / RevenueSplits / Enrollments / Certificates
- ❌ Hard delete ข้อมูลการเงินหรือสิทธิ์เรียน — ใช้สถานะ (Cancelled/Refunded/Revoked)
- ❌ ใช้ `InMemory` provider ในเทสต์ — ใช้ Testcontainers MSSQL
- ❌ เขียนค่า denormalized (`EnrollmentCount`, `RatingAverage`, ...) จากหลายที่ — ต้องผ่าน updater ตัวเดียวเท่านั้น
- ❌ เก็บข้อมูลอ่อนไหว (เลขบัญชี, เลขผู้เสียภาษี) เป็น plaintext
