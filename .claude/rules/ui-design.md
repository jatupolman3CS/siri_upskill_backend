# UI / UX Rules

## ใช้ skill `ui-ux-pro-max` ก่อนเสมอ

งานที่ **ต้อง** เรียก skill นี้ก่อนลงมือ: ออกแบบหน้าใหม่, สร้าง/รื้อ component, เลือกสี-ฟอนต์-spacing-layout, ทำ navigation/animation/responsive, รีวิว UI, แก้ปัญหา a11y
งานที่ **ไม่ต้อง**: backend logic, API/DB, infra, script ที่ไม่มีผลกับหน้าตา

> ⚠️ **ข้อจำกัดของเครื่องนี้:** สคริปต์ค้นหาของ skill ต้องใช้ Python แต่เครื่องนี้ยังไม่มี Python ติดตั้ง
> ถ้าจะใช้ search ให้ติดตั้งก่อน: `winget install Python.Python.3.13`
> ระหว่างที่ยังไม่มี Python ให้อ่าน `references/quick-reference.md` และ `references/pro-rules.md` ในโฟลเดอร์ skill โดยตรงแทน และบอก user ว่าใช้โหมด fallback อยู่

## Design system (ตัดสินใจครั้งเดียว ใช้ทั้งระบบ)

- Token อยู่ที่ `frontend/src/app/styles/tokens.css` — **ห้ามเขียน hex ดิบใน component** ใช้ CSS variable/Tailwind token เท่านั้น
- รองรับ light + dark ตั้งแต่ต้น: นิยาม palette เต็มที่ `:root`, override เฉพาะ token ที่ต่างใน dark
- Spacing scale 4px, radius และ shadow มีแค่ชุดที่กำหนดไว้ ห้ามคิดค่าใหม่รายหน้า
- Icon ใช้ SVG ชุดเดียวทั้งระบบ (Phosphor/Lucide) — **ห้ามใช้ emoji แทน icon**
- ฟอนต์ต้องรองรับภาษาไทยครบน้ำหนัก (แนะนำ IBM Plex Sans Thai / Noto Sans Thai / LINE Seed Sans TH) — เช็ครูปสระบน-ล่างไม่ชนกันที่ line-height ที่ใช้จริง

## Accessibility (ไม่ใช่ option)

- Contrast ข้อความปกติ ≥ 4.5:1, ข้อความใหญ่ ≥ 3:1
- **ห้ามลบ focus ring** ถ้าจะเปลี่ยนต้องทำให้เห็นชัดกว่าเดิม
- ทุก interactive element ต้องกดด้วยคีย์บอร์ดได้ และมีลำดับ tab ที่สมเหตุสมผล
- ปุ่มที่มีแต่ icon ต้องมี `aria-label`
- Touch target ≥ 44×44px และเว้นระยะกัน ≥ 8px
- Form: label ต้องมองเห็น (placeholder ไม่นับเป็น label), error อยู่ติดกับ field
- รองรับ `prefers-reduced-motion` ในทุก animation
- Video player: มี caption track, keyboard shortcut (space/←/→/f/m), และประกาศสถานะผ่าน aria-live

## Layout & responsive

- Mobile-first; breakpoint หลัก 640 / 768 / 1024 / 1280
- ห้ามเกิด horizontal scroll ที่ระดับ body — ตาราง/โค้ด/กราฟกว้างต้อง scroll ในกล่องของตัวเอง
- จองพื้นที่ให้รูปและ block ที่โหลดทีหลังเสมอ (CLS < 0.1)
- Bottom nav บนมือถือไม่เกิน 5 รายการ; รองรับ safe area ของ iOS

## หน้าจอที่ต้องพิถีพิถันเป็นพิเศษ

| หน้า | จุดชี้เป็นชี้ตาย |
|-----|-----------------|
| Course detail | syllabus ที่ scan ง่าย, free preview เด่น, ราคา+ปุ่มซื้อ sticky บนมือถือ, social proof |
| Video player (`/learn`) | ควบคุมง่ายด้วยคีย์บอร์ด, resume ชัด, ความคืบหน้าเห็นตลอด, watermark ต้องไม่บังเนื้อหาสำคัญ |
| Checkout | ขั้นตอนน้อยที่สุด, สรุปยอดชัด, QR PromptPay อ่านง่ายบนมือถือ, บอกสถานะรอจ่ายแบบ real-time |
| Course builder | drag ต้องมี affordance ชัด + คีย์บอร์ดก็ย้ายได้, autosave พร้อม indicator, undo ได้ |
| Search/catalog | filter ไม่รีเซ็ตเมื่อกด back, จำนวนผลลัพธ์เห็นทันที, empty state บอกทางไปต่อ |

## Anti-patterns ที่ห้ามทำ

- ใส่ animation ความยาวเดียวกันทุกอย่าง / animate `width`,`height`,`top`,`left` (ใช้ `transform`,`opacity`)
- ผสม style หลายภาษาในหน้าเดียว (flat + skeuomorphic + glassmorphism ปนกัน)
- ตัวหนังสือ body เล็กกว่า 14px (ภาษาไทยควร ≥ 16px เพราะมีสระบน-ล่าง)
- เทาบนเทาแบบ contrast ต่ำ, ใช้สีอย่างเดียวสื่อความหมาย (ต้องมี icon/ข้อความกำกับ)
- state ที่ไม่มี feedback: กดแล้วเงียบ, โหลดแล้วไม่มี skeleton, error แล้วไม่บอกว่าต้องทำอะไรต่อ
