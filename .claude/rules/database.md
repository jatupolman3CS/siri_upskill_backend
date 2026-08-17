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
