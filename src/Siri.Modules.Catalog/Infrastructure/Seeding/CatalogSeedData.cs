using Siri.Modules.Catalog.Domain;

namespace Siri.Modules.Catalog.Infrastructure.Seeding;

/// <summary>
/// Builds the fixed sample-catalog data set for task P1-30 ("seed คอร์สตัวอย่าง: 3 หมวดหลัก + 20 คอร์ส +
/// instructor 5 คน (ข้อมูลไทยจริง ไม่ใช่ lorem)"). Pure function of nothing at all — no options, no
/// database, no clock — so the data set itself is unit-testable on its own (backend.md: "Unit test:
/// domain logic ... บังคับ"), same split <see cref="Identity.Infrastructure.Seeding.IdentitySeedData"/>
/// already establishes between "what the seed data looks like" (here) and "how it gets persisted"
/// (<see cref="CatalogSeeder"/>).
/// <para>
/// <b>Genuinely fictional but genuinely realistic</b> — every category, instructor persona, and course
/// below is invented for this seed (not a real business/person), but written the way a real Thai
/// e-learning marketplace's catalog reads: real course topics with real Thai wording, real-shaped Thai
/// instructor names/bios/credentials, and price points in the range actual Thai course platforms use
/// (690–2,490 THB). None of this is lorem-ipsum or placeholder text — that is the task's explicit
/// requirement.
/// </para>
/// <para>
/// <b>Distribution:</b> 3 root categories, 5 instructor personas, 20 courses (7 Marketing &amp; Business, 7
/// Programming &amp; Technology, 6 Finance &amp; Investment) — every course maps to exactly one category and
/// one instructor, validated to actually match by <see cref="CatalogSeedDataTests"/>. Every course gets 2
/// sections of 2 episodes each (4 episodes total), the first episode always marked as a free preview —
/// enough for a believable syllabus accordion (P1-22) without hand-authoring an unbounded amount of
/// content. All 20 end up <c>Published</c> (see <see cref="CatalogSeeder"/>), all lifetime access (no
/// <c>AccessDurationDays</c>) — matches CLAUDE.md's own framing of this platform's business model
/// ("ผู้เรียนซื้อคอร์สวิดีโอเข้าถึงระยะยาว").
/// </para>
/// </summary>
public static class CatalogSeedData
{
    public static IReadOnlyList<CategorySeedSpec> BuildCategories() =>
    [
        new CategorySeedSpec("marketing-business", "การตลาดและธุรกิจ", "Marketing & Business", "megaphone"),
        new CategorySeedSpec("programming-technology", "การเขียนโปรแกรมและเทคโนโลยี", "Programming & Technology", "code"),
        new CategorySeedSpec("finance-investment", "การเงินและการลงทุน", "Finance & Investment", "coins"),
    ];

    public static IReadOnlyList<InstructorSeedSpec> BuildInstructors() =>
    [
        new InstructorSeedSpec(
            "instructor1.seed@example.test",
            "สมชาย รุ่งเรืองกิจ",
            "ที่ปรึกษาการตลาดดิจิทัล 12 ปี อดีต Marketing Manager บริษัท FMCG ชั้นนำ",
            "อดีต Marketing Manager บริษัท FMCG ชั้นนำในประเทศไทย ปัจจุบันเป็นที่ปรึกษาด้านการตลาดดิจิทัลให้กับธุรกิจ SME กว่า 50 แห่ง เชี่ยวชาญด้านการวางกลยุทธ์การตลาดออนไลน์และโฆษณาบนโซเชียลมีเดีย"),
        new InstructorSeedSpec(
            "instructor2.seed@example.test",
            "ปิยะดา วัฒนสิน",
            "ผู้ก่อตั้งธุรกิจ SME 2 แห่ง วิทยากรด้านการบริหารธุรกิจขนาดย่อม",
            "ผู้ก่อตั้งและบริหารธุรกิจ SME มาแล้ว 2 แห่งตลอด 15 ปีที่ผ่านมา ปัจจุบันเป็นวิทยากรและที่ปรึกษาด้านการบริหารธุรกิจขนาดย่อมให้กับหน่วยงานภาครัฐและเอกชนหลายแห่ง"),
        new InstructorSeedSpec(
            "instructor3.seed@example.test",
            "ธนกฤต ศรีสุวรรณ",
            "Senior Software Engineer 10+ ปี อดีตวิศวกรบริษัทเทคโนโลยีระดับประเทศ",
            "Senior Software Engineer ที่มีประสบการณ์กว่า 10 ปีในบริษัทเทคโนโลยีระดับประเทศ เชี่ยวชาญด้านการพัฒนาเว็บแอปพลิเคชันและการสอนพื้นฐานการเขียนโปรแกรมให้กับผู้เริ่มต้น"),
        new InstructorSeedSpec(
            "instructor4.seed@example.test",
            "ณัฐวุฒิ เจริญพงศ์",
            "Data Engineer และผู้เชี่ยวชาญ Python สอนเขียนโปรแกรมมากว่า 8 ปี",
            "Data Engineer และผู้เชี่ยวชาญด้าน Python ที่สอนเขียนโปรแกรมและวิเคราะห์ข้อมูลมากว่า 8 ปี เคยผ่านงานด้านข้อมูลให้กับองค์กรทั้งภาคการเงินและอีคอมเมิร์ซ"),
        new InstructorSeedSpec(
            "instructor5.seed@example.test",
            "กมลวรรณ อินทรสุวรรณ",
            "นักวางแผนการเงิน CFP® ที่ปรึกษาการลงทุนอิสระ 9 ปี",
            "นักวางแผนการเงิน CFP® และที่ปรึกษาการลงทุนอิสระมากว่า 9 ปี ให้คำปรึกษาด้านการวางแผนการเงินส่วนบุคคลและการลงทุนแก่ลูกค้ามาแล้วกว่าพันราย"),
    ];

    public static IReadOnlyList<CourseSeedSpec> BuildCourses() =>
    [
        // ---- Marketing & Business — สมชาย (4 courses) --------------------------------------------
        new CourseSeedSpec(
            "marketing-business", "instructor1.seed@example.test",
            "การตลาดดิจิทัลสำหรับธุรกิจ SME ฉบับเริ่มต้น",
            "วางกลยุทธ์การตลาดออนไลน์ตั้งแต่ศูนย์ เหมาะสำหรับเจ้าของธุรกิจและนักการตลาดมือใหม่",
            "คอร์สนี้ออกแบบมาสำหรับเจ้าของธุรกิจ SME และนักการตลาดมือใหม่ที่ต้องการเริ่มทำการตลาดออนไลน์อย่างมีทิศทาง ไม่ใช่แค่ลองผิดลองถูก เรียนรู้ตั้งแต่การวางกลยุทธ์ เลือกช่องทาง ไปจนถึงการวัดผลด้วยเครื่องมือฟรีที่หาได้จริง",
            CourseLevel.Beginner, 990m, 1990m,
            ["เข้าใจภาพรวมช่องทางการตลาดดิจิทัลทั้งหมดและเลือกช่องทางที่เหมาะกับธุรกิจตัวเอง", "วางแผนงบประมาณการตลาดออนไลน์เดือนแรกได้ด้วยตัวเอง", "วัดผลแคมเปญการตลาดด้วยเครื่องมือฟรีที่หาได้จริง"],
            ["มีธุรกิจหรือไอเดียธุรกิจที่ต้องการทำการตลาด", "ไม่จำเป็นต้องมีพื้นฐานการตลาดมาก่อน"],
            [
                new CourseSectionSeedSpec("ปูพื้นฐานการตลาดดิจิทัล",
                [
                    new CourseEpisodeSeedSpec("การตลาดดิจิทัลคืออะไร ทำไมธุรกิจ SME ต้องให้ความสำคัญ", 720),
                    new CourseEpisodeSeedSpec("รู้จักช่องทางการตลาดออนไลน์ทั้งหมดในภาพเดียว", 900),
                ]),
                new CourseSectionSeedSpec("ลงมือวางแผนจริง",
                [
                    new CourseEpisodeSeedSpec("วางแผนงบประมาณการตลาดออนไลน์เดือนแรก", 840),
                    new CourseEpisodeSeedSpec("วัดผลแคมเปญด้วยเครื่องมือฟรีที่หาได้จริง", 960),
                ]),
            ]),
        new CourseSeedSpec(
            "marketing-business", "instructor1.seed@example.test",
            "Facebook Ads และ Instagram Ads แบบมืออาชีพ",
            "ยิงแอดอย่างไรให้คุ้มทุน ตั้งแต่ตั้งแคมเปญจนถึงวิเคราะห์ผลลัพธ์",
            "เจาะลึกการยิงโฆษณา Facebook และ Instagram แบบมืออาชีพ ตั้งแต่การตั้งค่าบัญชีโฆษณา เลือกกลุ่มเป้าหมาย ไปจนถึงการอ่านผลลัพธ์เพื่อปรับปรุงแคมเปญให้คุ้มค่ากับทุกบาทที่ลงทุนไป",
            CourseLevel.Intermediate, 1490m, 2490m,
            ["ตั้งแคมเปญโฆษณาบน Facebook และ Instagram ได้อย่างถูกต้อง", "เลือกกลุ่มเป้าหมายและงบประมาณให้คุ้มค่าที่สุด", "อ่านและวิเคราะห์ผลลัพธ์แคมเปญเพื่อปรับปรุงอย่างต่อเนื่อง"],
            ["มีเพจ Facebook ธุรกิจของตัวเอง", "ควรมีพื้นฐานการตลาดดิจิทัลเบื้องต้นมาก่อน"],
            [
                new CourseSectionSeedSpec("เริ่มต้นตั้งแคมเปญโฆษณา",
                [
                    new CourseEpisodeSeedSpec("โครงสร้างบัญชีโฆษณา Facebook Ads Manager", 660),
                    new CourseEpisodeSeedSpec("สร้างแคมเปญแรกและเลือกวัตถุประสงค์ให้ถูกต้อง", 1080),
                ]),
                new CourseSectionSeedSpec("ยิงแอดให้คุ้มทุน",
                [
                    new CourseEpisodeSeedSpec("เจาะกลุ่มเป้าหมายและตั้งงบประมาณอย่างมีประสิทธิภาพ", 900),
                    new CourseEpisodeSeedSpec("อ่านผลลัพธ์แคมเปญและปรับปรุงโฆษณาต่อเนื่อง", 1140),
                ]),
            ]),
        new CourseSeedSpec(
            "marketing-business", "instructor1.seed@example.test",
            "SEO เบื้องต้น: ทำให้เว็บไซต์ติดหน้าแรก Google",
            "เทคนิค SEO ที่ใช้ได้จริงสำหรับเว็บไซต์ธุรกิจขนาดเล็กถึงกลาง",
            "เรียนรู้เทคนิค SEO ที่ใช้ได้จริงกับเว็บไซต์ธุรกิจขนาดเล็กถึงกลาง ตั้งแต่หลักการทำงานของ Google การค้นหาคีย์เวิร์ด ไปจนถึงการทำ On-page SEO ด้วยตัวเองโดยไม่ต้องพึ่งเอเจนซี่",
            CourseLevel.Beginner, 890m, 1590m,
            ["เข้าใจหลักการทำงานของ Google และปัจจัยการจัดอันดับเว็บไซต์", "ทำ On-page SEO ให้เว็บไซต์ของตัวเองได้จริง", "ค้นหาคีย์เวิร์ดที่มีโอกาสติดอันดับสูงสำหรับธุรกิจ"],
            ["มีเว็บไซต์ของตัวเองหรือธุรกิจที่ต้องการโปรโมท", "ไม่จำเป็นต้องเขียนโค้ดเป็น"],
            [
                new CourseSectionSeedSpec("เข้าใจกลไกการค้นหา",
                [
                    new CourseEpisodeSeedSpec("Google จัดอันดับเว็บไซต์อย่างไร", 780),
                    new CourseEpisodeSeedSpec("ค้นหาคีย์เวิร์ดที่มีโอกาสติดอันดับสูง", 900),
                ]),
                new CourseSectionSeedSpec("ลงมือทำ SEO จริง",
                [
                    new CourseEpisodeSeedSpec("ทำ On-page SEO ให้เว็บไซต์ของตัวเอง", 1020),
                    new CourseEpisodeSeedSpec("สร้างลิงก์และสัญญาณความน่าเชื่อถือให้เว็บไซต์", 780),
                ]),
            ]),
        new CourseSeedSpec(
            "marketing-business", "instructor1.seed@example.test",
            "การเขียน Content Marketing ที่ขายได้จริง",
            "เทคนิคเขียนคอนเทนต์โน้มน้าวใจลูกค้า ตั้งแต่โพสต์โซเชียลถึงอีเมล",
            "ฝึกเขียนคอนเทนต์ที่ไม่ใช่แค่สวยงามแต่ขายได้จริง ครอบคลุมทั้งโพสต์โซเชียลมีเดีย บทความ และอีเมลการตลาด พร้อมเทคนิควางแผนคอนเทนต์อย่างเป็นระบบตลอดทั้งเดือน",
            CourseLevel.Intermediate, 1290m, 2190m,
            ["เขียนโพสต์โซเชียลมีเดียที่ดึงดูดและกระตุ้นยอดขาย", "วางโครงสร้างบทความและอีเมลการตลาดอย่างมีหลักการ", "สร้าง Content Calendar ที่ใช้งานได้จริงในธุรกิจ"],
            ["เขียนภาษาไทยได้คล่อง", "มีสินค้าหรือบริการที่ต้องการโปรโมท"],
            [
                new CourseSectionSeedSpec("หลักการเขียนที่ขายได้",
                [
                    new CourseEpisodeSeedSpec("โครงสร้างคอนเทนต์ที่โน้มน้าวใจลูกค้า", 900),
                    new CourseEpisodeSeedSpec("เขียนโพสต์โซเชียลมีเดียให้คนหยุดสกรอลดู", 660),
                ]),
                new CourseSectionSeedSpec("วางแผนคอนเทนต์อย่างเป็นระบบ",
                [
                    new CourseEpisodeSeedSpec("สร้าง Content Calendar ที่ใช้งานได้จริง", 780),
                    new CourseEpisodeSeedSpec("เขียนอีเมลการตลาดที่เปิดอ่านและคลิกจริง", 900),
                ]),
            ]),

        // ---- Marketing & Business — ปิยะดา (3 courses) -------------------------------------------
        new CourseSeedSpec(
            "marketing-business", "instructor2.seed@example.test",
            "เริ่มต้นธุรกิจ SME อย่างไรให้อยู่รอด",
            "บทเรียนจากประสบการณ์จริงในการก่อตั้งและบริหารธุรกิจขนาดย่อม",
            "รวมบทเรียนจากประสบการณ์จริงในการก่อตั้งและบริหารธุรกิจ SME ตั้งแต่การตรวจสอบไอเดียก่อนลงทุน การเขียนแผนธุรกิจที่ใช้งานได้จริง ไปจนถึงข้อผิดพลาดที่ทำให้ธุรกิจส่วนใหญ่ไปไม่รอด",
            CourseLevel.Beginner, 1190m, 1990m,
            ["วางแผนธุรกิจตั้งแต่ไอเดียจนถึงแผนปฏิบัติการจริง", "หลีกเลี่ยงข้อผิดพลาดที่ทำให้ธุรกิจ SME ส่วนใหญ่ล้มเหลว", "สร้างโมเดลธุรกิจที่ยั่งยืนในระยะยาว"],
            ["มีไอเดียธุรกิจที่ต้องการเริ่มต้นหรือกำลังดำเนินธุรกิจอยู่แล้ว"],
            [
                new CourseSectionSeedSpec("จากไอเดียสู่แผนธุรกิจ",
                [
                    new CourseEpisodeSeedSpec("ตรวจสอบไอเดียธุรกิจก่อนลงทุนจริง", 900),
                    new CourseEpisodeSeedSpec("เขียนแผนธุรกิจฉบับใช้งานได้จริง ไม่ใช่แค่ทฤษฎี", 1080),
                ]),
                new CourseSectionSeedSpec("อยู่รอดในปีแรก",
                [
                    new CourseEpisodeSeedSpec("ข้อผิดพลาดที่ทำให้ธุรกิจ SME ส่วนใหญ่ล้มเหลว", 840),
                    new CourseEpisodeSeedSpec("สร้างโมเดลธุรกิจที่ยั่งยืนในระยะยาว", 780),
                ]),
            ]),
        new CourseSeedSpec(
            "marketing-business", "instructor2.seed@example.test",
            "การบริหารกระแสเงินสดสำหรับเจ้าของธุรกิจ",
            "อ่านงบการเงินและวางแผนกระแสเงินสดไม่ให้ธุรกิจสะดุด",
            "เรียนรู้การอ่านงบการเงินและบริหารกระแสเงินสดสำหรับเจ้าของธุรกิจที่ไม่มีพื้นฐานบัญชี เพื่อวางแผนล่วงหน้าและไม่ให้ธุรกิจสะดุดเพราะปัญหาสภาพคล่อง",
            CourseLevel.Intermediate, 1390m, 2290m,
            ["อ่านงบกระแสเงินสดและงบการเงินพื้นฐานได้", "วางแผนกระแสเงินสดล่วงหน้าเพื่อไม่ให้ธุรกิจขาดสภาพคล่อง", "รับมือกับช่วงเวลาที่ธุรกิจมีรายจ่ายมากกว่ารายรับ"],
            ["เป็นเจ้าของธุรกิจหรือผู้บริหารที่ดูแลด้านการเงิน", "ไม่จำเป็นต้องมีพื้นฐานบัญชีมาก่อน"],
            [
                new CourseSectionSeedSpec("อ่านงบการเงินให้เป็น",
                [
                    new CourseEpisodeSeedSpec("งบกระแสเงินสดคืออะไร สำคัญอย่างไรกับธุรกิจ", 720),
                    new CourseEpisodeSeedSpec("อ่านงบการเงินพื้นฐาน 3 ตัวที่เจ้าของธุรกิจต้องรู้", 1020),
                ]),
                new CourseSectionSeedSpec("วางแผนกระแสเงินสด",
                [
                    new CourseEpisodeSeedSpec("พยากรณ์กระแสเงินสดล่วงหน้า 3-6 เดือน", 900),
                    new CourseEpisodeSeedSpec("รับมือช่วงรายจ่ายมากกว่ารายรับโดยไม่กู้เพิ่ม", 840),
                ]),
            ]),
        new CourseSeedSpec(
            "marketing-business", "instructor2.seed@example.test",
            "เจรจาต่อรองทางธุรกิจให้ได้ผลลัพธ์ที่ต้องการ",
            "เทคนิคการเจรจากับคู่ค้า ซัพพลายเออร์ และนักลงทุน",
            "พัฒนาทักษะการเจรจาต่อรองทางธุรกิจตั้งแต่การเตรียมตัวก่อนเข้าเจรจา ไปจนถึงเทคนิครับมือสถานการณ์กดดัน เพื่อให้ได้ผลลัพธ์แบบ Win-Win กับคู่ค้า ซัพพลายเออร์ หรือนักลงทุน",
            CourseLevel.AllLevels, 990m, 1690m,
            ["เตรียมตัวก่อนเข้าเจรจาธุรกิจทุกครั้งอย่างมีระบบ", "ใช้เทคนิคเจรจาต่อรองที่ได้ผลลัพธ์แบบ Win-Win", "รับมือกับสถานการณ์เจรจาที่กดดันหรือเสียเปรียบ"],
            ["ไม่จำเป็นต้องมีประสบการณ์เจรจาธุรกิจมาก่อน"],
            [
                new CourseSectionSeedSpec("เตรียมตัวก่อนเจรจา",
                [
                    new CourseEpisodeSeedSpec("วิเคราะห์อีกฝ่ายก่อนเข้าเจรจาทุกครั้ง", 660),
                    new CourseEpisodeSeedSpec("ตั้งเป้าหมายและจุดยืนต่ำสุดที่ยอมรับได้", 720),
                ]),
                new CourseSectionSeedSpec("เทคนิคเจรจาแบบมืออาชีพ",
                [
                    new CourseEpisodeSeedSpec("เทคนิคเจรจาแบบ Win-Win ที่ใช้ได้จริง", 900),
                    new CourseEpisodeSeedSpec("รับมือสถานการณ์เจรจาที่กดดันหรือเสียเปรียบ", 780),
                ]),
            ]),

        // ---- Programming & Technology — ธนกฤต (4 courses) ----------------------------------------
        new CourseSeedSpec(
            "programming-technology", "instructor3.seed@example.test",
            "เขียนโปรแกรมด้วย Python สำหรับผู้เริ่มต้น",
            "ปูพื้นฐานการเขียนโปรแกรมตั้งแต่ศูนย์ด้วยภาษาที่เรียนง่ายที่สุด",
            "ปูพื้นฐานการเขียนโปรแกรมตั้งแต่ศูนย์ด้วยภาษา Python ภาษาที่เรียนง่ายที่สุดสำหรับผู้เริ่มต้น เหมาะสำหรับคนที่ไม่เคยเขียนโปรแกรมมาก่อนและต้องการปูทางไปสายเทคโนโลยี",
            CourseLevel.Beginner, 1290m, 2190m,
            ["เข้าใจหลักการเขียนโปรแกรมเบื้องต้นและไวยากรณ์ภาษา Python", "เขียนโปรแกรมแก้ปัญหาเบื้องต้นด้วยตัวเองได้", "ต่อยอดไปเรียนสาย Data หรือ Web Development ได้ทันที"],
            ["มีคอมพิวเตอร์ที่ติดตั้งโปรแกรมได้", "ไม่จำเป็นต้องมีพื้นฐานการเขียนโปรแกรมมาก่อน"],
            [
                new CourseSectionSeedSpec("พื้นฐานภาษา Python",
                [
                    new CourseEpisodeSeedSpec("ติดตั้งเครื่องมือและรันโปรแกรม Python แรก", 600),
                    new CourseEpisodeSeedSpec("ตัวแปร ชนิดข้อมูล และการดำเนินการพื้นฐาน", 900),
                ]),
                new CourseSectionSeedSpec("เขียนโปรแกรมแก้ปัญหาจริง",
                [
                    new CourseEpisodeSeedSpec("เงื่อนไขและการวนซ้ำ (if, for, while)", 960),
                    new CourseEpisodeSeedSpec("ฟังก์ชันและการจัดการข้อผิดพลาดเบื้องต้น", 840),
                ]),
            ]),
        new CourseSeedSpec(
            "programming-technology", "instructor3.seed@example.test",
            "พัฒนาเว็บแอปพลิเคชันด้วย JavaScript และ React",
            "สร้างเว็บแอปสมัยใหม่ตั้งแต่ component แรกจนถึง deploy ขึ้นจริง",
            "สร้างเว็บแอปพลิเคชันสมัยใหม่ด้วย JavaScript และ React ตั้งแต่ Component แรกจนถึงการ Deploy ขึ้นใช้งานจริงบนอินเทอร์เน็ต เหมาะสำหรับผู้ที่มีพื้นฐาน HTML/CSS/JavaScript มาก่อน",
            CourseLevel.Intermediate, 2490m, 3990m,
            ["สร้าง Component และจัดการ State ด้วย React ได้อย่างถูกต้อง", "เชื่อมต่อเว็บแอปกับ API ภายนอก", "Deploy เว็บแอปพลิเคชันที่สร้างขึ้นให้ใช้งานได้จริงบนอินเทอร์เน็ต"],
            ["มีพื้นฐาน HTML/CSS/JavaScript มาก่อน", "มีคอมพิวเตอร์ที่ติดตั้งโปรแกรมได้"],
            [
                new CourseSectionSeedSpec("พื้นฐาน React",
                [
                    new CourseEpisodeSeedSpec("สร้าง Component แรกและเข้าใจ JSX", 780),
                    new CourseEpisodeSeedSpec("จัดการ State และ Props อย่างถูกวิธี", 1020),
                ]),
                new CourseSectionSeedSpec("สร้างเว็บแอปให้ใช้งานได้จริง",
                [
                    new CourseEpisodeSeedSpec("เชื่อมต่อเว็บแอปกับ API ภายนอก", 1140),
                    new CourseEpisodeSeedSpec("Deploy เว็บแอปให้ใช้งานได้จริงบนอินเทอร์เน็ต", 900),
                ]),
            ]),
        new CourseSeedSpec(
            "programming-technology", "instructor3.seed@example.test",
            "พื้นฐานโครงสร้างข้อมูลและอัลกอริทึม",
            "เตรียมสอบสัมภาษณ์งานสายเทคโนโลยีด้วยพื้นฐานที่แน่น",
            "ปูพื้นฐานโครงสร้างข้อมูลและอัลกอริทึมที่จำเป็นสำหรับสายเทคโนโลยี พร้อมฝึกวิเคราะห์ความซับซ้อนและแก้โจทย์สไตล์สัมภาษณ์งาน เตรียมความพร้อมก่อนสมัครงานสาย Developer",
            CourseLevel.Intermediate, 1990m, 3290m,
            ["เข้าใจโครงสร้างข้อมูลพื้นฐาน เช่น Array, Linked List, Stack, Queue", "วิเคราะห์ความซับซ้อนของอัลกอริทึม (Big O) ได้", "แก้โจทย์สัมภาษณ์งานสายเทคโนโลยีได้อย่างมั่นใจ"],
            ["มีพื้นฐานการเขียนโปรแกรมภาษาใดก็ได้มาก่อน"],
            [
                new CourseSectionSeedSpec("โครงสร้างข้อมูลพื้นฐาน",
                [
                    new CourseEpisodeSeedSpec("Array, Linked List และการเลือกใช้ให้เหมาะกับงาน", 900),
                    new CourseEpisodeSeedSpec("Stack, Queue และตัวอย่างการใช้งานจริง", 780),
                ]),
                new CourseSectionSeedSpec("วิเคราะห์และแก้โจทย์",
                [
                    new CourseEpisodeSeedSpec("วิเคราะห์ความซับซ้อนของอัลกอริทึมด้วย Big O", 960),
                    new CourseEpisodeSeedSpec("ฝึกแก้โจทย์สัมภาษณ์งานสายเทคโนโลยี", 1200),
                ]),
            ]),
        new CourseSeedSpec(
            "programming-technology", "instructor3.seed@example.test",
            "Git และ GitHub สำหรับทีมพัฒนาซอฟต์แวร์",
            "จัดการเวอร์ชันโค้ดและทำงานร่วมกับทีมอย่างมืออาชีพ",
            "เรียนรู้การใช้ Git และ GitHub สำหรับการทำงานร่วมกับทีมพัฒนาซอฟต์แวร์ ตั้งแต่คำสั่งพื้นฐาน การจัดการ Branch ไปจนถึงการแก้ปัญหา Merge Conflict ที่เจอบ่อยในงานจริง",
            CourseLevel.Beginner, 690m, 1190m,
            ["ใช้คำสั่ง Git พื้นฐานจัดการเวอร์ชันโค้ดได้คล่อง", "ทำงานร่วมกับทีมผ่าน Branch และ Pull Request", "แก้ปัญหา Merge Conflict ที่พบบ่อยในการทำงานจริง"],
            ["มีคอมพิวเตอร์ที่ติดตั้งโปรแกรมได้", "มีพื้นฐานการเขียนโปรแกรมเบื้องต้น"],
            [
                new CourseSectionSeedSpec("พื้นฐาน Git",
                [
                    new CourseEpisodeSeedSpec("เริ่มต้นใช้งาน Git และคำสั่งพื้นฐานที่ต้องรู้", 660),
                    new CourseEpisodeSeedSpec("สร้างและจัดการ Branch อย่างเป็นระบบ", 720),
                ]),
                new CourseSectionSeedSpec("ทำงานเป็นทีมด้วย GitHub",
                [
                    new CourseEpisodeSeedSpec("ทำงานร่วมกับทีมผ่าน Pull Request", 840),
                    new CourseEpisodeSeedSpec("แก้ปัญหา Merge Conflict ที่พบบ่อยในงานจริง", 780),
                ]),
            ]),

        // ---- Programming & Technology — ณัฐวุฒิ (3 courses) --------------------------------------
        new CourseSeedSpec(
            "programming-technology", "instructor4.seed@example.test",
            "วิเคราะห์ข้อมูลด้วย Python และ Pandas",
            "แปลงข้อมูลดิบให้เป็นข้อมูลเชิงลึกที่ใช้ตัดสินใจทางธุรกิจได้",
            "ใช้ Python และไลบรารี Pandas แปลงข้อมูลดิบให้เป็นข้อมูลเชิงลึกที่ใช้ตัดสินใจทางธุรกิจได้ ครอบคลุมตั้งแต่การทำความสะอาดข้อมูลไปจนถึงการสร้างกราฟสื่อสารผลลัพธ์",
            CourseLevel.Intermediate, 1790m, 2990m,
            ["ใช้ไลบรารี Pandas จัดการและทำความสะอาดข้อมูลได้", "สร้างกราฟและ Visualization เพื่อสื่อสารข้อมูลเชิงลึก", "วิเคราะห์ชุดข้อมูลจริงเพื่อสนับสนุนการตัดสินใจทางธุรกิจ"],
            ["มีพื้นฐานภาษา Python เบื้องต้น"],
            [
                new CourseSectionSeedSpec("จัดการข้อมูลด้วย Pandas",
                [
                    new CourseEpisodeSeedSpec("อ่านและทำความสะอาดข้อมูลด้วย Pandas DataFrame", 900),
                    new CourseEpisodeSeedSpec("กรอง จัดกลุ่ม และสรุปข้อมูลตามที่ต้องการ", 1020),
                ]),
                new CourseSectionSeedSpec("สื่อสารข้อมูลด้วยภาพ",
                [
                    new CourseEpisodeSeedSpec("สร้างกราฟ Visualization ที่สื่อสารข้อมูลเชิงลึก", 780),
                    new CourseEpisodeSeedSpec("วิเคราะห์ชุดข้อมูลจริงเพื่อสนับสนุนการตัดสินใจ", 1140),
                ]),
            ]),
        new CourseSeedSpec(
            "programming-technology", "instructor4.seed@example.test",
            "SQL สำหรับการวิเคราะห์ข้อมูลฉบับมือใหม่",
            "เขียนคำสั่ง SQL ดึงและวิเคราะห์ข้อมูลจากฐานข้อมูลจริง",
            "เรียนรู้การเขียน SQL ตั้งแต่พื้นฐานจนสามารถดึงและวิเคราะห์ข้อมูลจากฐานข้อมูลจริงได้ด้วยตัวเอง เหมาะสำหรับผู้เริ่มต้นสายข้อมูลที่ไม่มีพื้นฐานเขียนโปรแกรมมาก่อน",
            CourseLevel.Beginner, 990m, 1690m,
            ["เขียนคำสั่ง SELECT, JOIN และ GROUP BY เพื่อดึงข้อมูลที่ต้องการ", "ออกแบบคำสั่ง SQL สำหรับวิเคราะห์ข้อมูลเชิงธุรกิจ", "เข้าใจโครงสร้างฐานข้อมูลเชิงสัมพันธ์เบื้องต้น"],
            ["ไม่จำเป็นต้องมีพื้นฐานการเขียนโปรแกรมมาก่อน"],
            [
                new CourseSectionSeedSpec("พื้นฐาน SQL",
                [
                    new CourseEpisodeSeedSpec("เขียนคำสั่ง SELECT และกรองข้อมูลด้วย WHERE", 660),
                    new CourseEpisodeSeedSpec("รวมข้อมูลหลายตารางด้วย JOIN", 960),
                ]),
                new CourseSectionSeedSpec("วิเคราะห์ข้อมูลเชิงธุรกิจ",
                [
                    new CourseEpisodeSeedSpec("สรุปข้อมูลด้วย GROUP BY และฟังก์ชันรวม", 840),
                    new CourseEpisodeSeedSpec("เขียน Query วิเคราะห์ข้อมูลเชิงธุรกิจจริง", 1080),
                ]),
            ]),
        new CourseSeedSpec(
            "programming-technology", "instructor4.seed@example.test",
            "สร้าง Dashboard วิเคราะห์ข้อมูลด้วย Power BI",
            "แปลงข้อมูลบริษัทให้เป็น Dashboard ที่ผู้บริหารอ่านแล้วตัดสินใจได้ทันที",
            "สร้าง Dashboard วิเคราะห์ข้อมูลด้วย Power BI ตั้งแต่การเชื่อมต่อข้อมูลจากหลายแหล่ง ไปจนถึงการออกแบบรายงานที่ผู้บริหารอ่านแล้วตัดสินใจได้ทันที",
            CourseLevel.Intermediate, 1590m, 2590m,
            ["เชื่อมต่อและแปลงข้อมูลจากหลายแหล่งเข้าสู่ Power BI", "ออกแบบ Dashboard ที่สื่อสารข้อมูลได้ชัดเจนและสวยงาม", "แชร์รายงานให้ทีมหรือผู้บริหารใช้งานได้จริง"],
            ["มีคอมพิวเตอร์ที่ติดตั้ง Power BI Desktop ได้ (Windows)"],
            [
                new CourseSectionSeedSpec("เตรียมและเชื่อมต่อข้อมูล",
                [
                    new CourseEpisodeSeedSpec("เชื่อมต่อและแปลงข้อมูลจากหลายแหล่งเข้าสู่ Power BI", 900),
                    new CourseEpisodeSeedSpec("จัดโครงสร้างข้อมูลให้พร้อมสำหรับสร้างรายงาน", 780),
                ]),
                new CourseSectionSeedSpec("สร้าง Dashboard ที่ใช้งานได้จริง",
                [
                    new CourseEpisodeSeedSpec("ออกแบบ Dashboard ที่สื่อสารข้อมูลได้ชัดเจน", 1020),
                    new CourseEpisodeSeedSpec("แชร์และเผยแพร่รายงานให้ทีมใช้งานจริง", 660),
                ]),
            ]),

        // ---- Finance & Investment — กมลวรรณ (6 courses) ------------------------------------------
        new CourseSeedSpec(
            "finance-investment", "instructor5.seed@example.test",
            "ลงทุนหุ้นเบื้องต้นสำหรับมือใหม่",
            "ปูพื้นฐานการลงทุนหุ้นตั้งแต่เปิดพอร์ตจนถึงอ่านงบการเงินเบื้องต้น",
            "ปูพื้นฐานการลงทุนหุ้นสำหรับมือใหม่ที่ไม่เคยลงทุนมาก่อน ตั้งแต่การเปิดพอร์ต เข้าใจกลไกตลาดหลักทรัพย์ ไปจนถึงการอ่านงบการเงินเบื้องต้นก่อนตัดสินใจลงทุนจริง",
            CourseLevel.Beginner, 890m, 1590m,
            ["เปิดพอร์ตและเข้าใจกลไกการซื้อขายหุ้นในตลาดหลักทรัพย์", "อ่านงบการเงินเบื้องต้นก่อนตัดสินใจลงทุน", "วางกลยุทธ์การลงทุนที่เหมาะกับความเสี่ยงที่รับได้"],
            ["ไม่จำเป็นต้องมีประสบการณ์ลงทุนมาก่อน"],
            [
                new CourseSectionSeedSpec("เริ่มต้นลงทุนหุ้น",
                [
                    new CourseEpisodeSeedSpec("เปิดพอร์ตและเข้าใจกลไกตลาดหลักทรัพย์", 720),
                    new CourseEpisodeSeedSpec("อ่านงบการเงินเบื้องต้นก่อนตัดสินใจลงทุน", 960),
                ]),
                new CourseSectionSeedSpec("วางกลยุทธ์การลงทุน",
                [
                    new CourseEpisodeSeedSpec("ประเมินความเสี่ยงที่ตัวเองรับได้", 660),
                    new CourseEpisodeSeedSpec("วางกลยุทธ์ลงทุนระยะยาวแบบ DCA", 840),
                ]),
            ]),
        new CourseSeedSpec(
            "finance-investment", "instructor5.seed@example.test",
            "วางแผนการเงินส่วนบุคคลให้มั่นคง",
            "จัดการรายรับรายจ่าย ตั้งเป้าหมายทางการเงิน และวางแผนเกษียณ",
            "จัดการเงินส่วนบุคคลให้เป็นระบบ ตั้งแต่การทำงบรายรับรายจ่าย สร้างเงินสำรองฉุกเฉิน ไปจนถึงการตั้งเป้าหมายทางการเงินระยะยาวและเริ่มต้นวางแผนเกษียณ",
            CourseLevel.Beginner, 790m, 1290m,
            ["จัดทำงบรายรับรายจ่ายส่วนบุคคลที่ใช้ได้จริง", "ตั้งเป้าหมายทางการเงินระยะสั้นและระยะยาวอย่างเป็นระบบ", "วางแผนเงินสำรองฉุกเฉินและเริ่มต้นวางแผนเกษียณ"],
            ["ไม่จำเป็นต้องมีพื้นฐานการเงินมาก่อน"],
            [
                new CourseSectionSeedSpec("จัดการเงินส่วนบุคคล",
                [
                    new CourseEpisodeSeedSpec("จัดทำงบรายรับรายจ่ายที่ใช้ได้จริง", 660),
                    new CourseEpisodeSeedSpec("สร้างเงินสำรองฉุกเฉินให้เพียงพอ", 600),
                ]),
                new CourseSectionSeedSpec("วางเป้าหมายระยะยาว",
                [
                    new CourseEpisodeSeedSpec("ตั้งเป้าหมายทางการเงินระยะสั้นและระยะยาว", 780),
                    new CourseEpisodeSeedSpec("เริ่มต้นวางแผนเกษียณตั้งแต่วันนี้", 720),
                ]),
            ]),
        new CourseSeedSpec(
            "finance-investment", "instructor5.seed@example.test",
            "กองทุนรวมและ ETF: เลือกลงทุนให้เหมาะกับตัวเอง",
            "ทำความเข้าใจกองทุนรวมและ ETF ก่อนตัดสินใจลงทุนจริง",
            "ทำความเข้าใจกองทุนรวมและ ETF แต่ละประเภทก่อนตัดสินใจลงทุนจริง พร้อมเทคนิคอ่านหนังสือชี้ชวนและเลือกกองทุนให้เหมาะกับเป้าหมายและความเสี่ยงที่รับได้",
            CourseLevel.Beginner, 990m, 1690m,
            ["เข้าใจความแตกต่างระหว่างกองทุนรวมประเภทต่าง ๆ และ ETF", "อ่านหนังสือชี้ชวนกองทุนและประเมินความเสี่ยงได้", "เลือกกองทุนที่เหมาะกับเป้าหมายและระดับความเสี่ยงของตัวเอง"],
            ["ไม่จำเป็นต้องมีประสบการณ์ลงทุนมาก่อน"],
            [
                new CourseSectionSeedSpec("รู้จักกองทุนรวมและ ETF",
                [
                    new CourseEpisodeSeedSpec("ความแตกต่างระหว่างกองทุนรวมประเภทต่าง ๆ", 780),
                    new CourseEpisodeSeedSpec("ETF คืออะไร ต่างจากกองทุนรวมทั่วไปอย่างไร", 660),
                ]),
                new CourseSectionSeedSpec("เลือกกองทุนให้เหมาะกับตัวเอง",
                [
                    new CourseEpisodeSeedSpec("อ่านหนังสือชี้ชวนและประเมินความเสี่ยงกองทุน", 900),
                    new CourseEpisodeSeedSpec("เลือกกองทุนให้เหมาะกับเป้าหมายของตัวเอง", 720),
                ]),
            ]),
        new CourseSeedSpec(
            "finance-investment", "instructor5.seed@example.test",
            "การวิเคราะห์งบการเงินเพื่อการลงทุน",
            "อ่านงบการเงินบริษัทให้ขาดก่อนตัดสินใจซื้อหุ้น",
            "เรียนรู้การอ่านงบการเงินของบริษัทจดทะเบียนอย่างละเอียด ตั้งแต่งบดุล งบกำไรขาดทุน ไปจนถึงการคำนวณอัตราส่วนทางการเงินเพื่อประเมินมูลค่าหุ้นก่อนตัดสินใจซื้อ",
            CourseLevel.Intermediate, 1690m, 2790m,
            ["อ่านงบดุล งบกำไรขาดทุน และงบกระแสเงินสดของบริษัทจดทะเบียน", "คำนวณอัตราส่วนทางการเงินที่สำคัญสำหรับการลงทุน", "ประเมินมูลค่าและความเสี่ยงของหุ้นก่อนตัดสินใจซื้อ"],
            ["มีพื้นฐานการลงทุนหุ้นเบื้องต้นมาก่อน"],
            [
                new CourseSectionSeedSpec("อ่านงบการเงินบริษัทจดทะเบียน",
                [
                    new CourseEpisodeSeedSpec("อ่านงบดุลและงบกำไรขาดทุนให้เข้าใจ", 960),
                    new CourseEpisodeSeedSpec("อ่านงบกระแสเงินสดของบริษัท", 840),
                ]),
                new CourseSectionSeedSpec("ประเมินมูลค่าเพื่อการลงทุน",
                [
                    new CourseEpisodeSeedSpec("คำนวณอัตราส่วนทางการเงินที่สำคัญ", 1020),
                    new CourseEpisodeSeedSpec("ประเมินมูลค่าหุ้นก่อนตัดสินใจซื้อ", 1140),
                ]),
            ]),
        new CourseSeedSpec(
            "finance-investment", "instructor5.seed@example.test",
            "ลดหย่อนภาษีอย่างถูกวิธีสำหรับมนุษย์เงินเดือน",
            "ใช้สิทธิลดหย่อนภาษีให้คุ้มค่าที่สุดตามกฎหมายจริง",
            "เรียนรู้การวางแผนภาษีอย่างถูกวิธีสำหรับมนุษย์เงินเดือน ใช้สิทธิลดหย่อนภาษีให้คุ้มค่าที่สุดตามกฎหมายจริง แทนการยื่นภาษีแบบเร่งรีบตอนใกล้กำหนด",
            CourseLevel.Beginner, 690m, 1190m,
            ["เข้าใจโครงสร้างภาษีเงินได้บุคคลธรรมดาและวิธีคำนวณ", "เลือกใช้สิทธิลดหย่อนภาษีที่เหมาะกับตัวเองได้อย่างคุ้มค่า", "วางแผนภาษีล่วงหน้าตลอดทั้งปีแทนการทำตอนใกล้ยื่นภาษี"],
            ["เป็นพนักงานประจำที่มีรายได้จากเงินเดือน"],
            [
                new CourseSectionSeedSpec("เข้าใจโครงสร้างภาษี",
                [
                    new CourseEpisodeSeedSpec("โครงสร้างภาษีเงินได้บุคคลธรรมดาและวิธีคำนวณ", 780),
                    new CourseEpisodeSeedSpec("สิทธิลดหย่อนภาษีที่มนุษย์เงินเดือนมักพลาด", 720),
                ]),
                new CourseSectionSeedSpec("วางแผนภาษีทั้งปี",
                [
                    new CourseEpisodeSeedSpec("เลือกใช้สิทธิลดหย่อนให้คุ้มค่าที่สุด", 660),
                    new CourseEpisodeSeedSpec("วางแผนภาษีล่วงหน้าแทนการทำตอนใกล้ยื่น", 600),
                ]),
            ]),
        new CourseSeedSpec(
            "finance-investment", "instructor5.seed@example.test",
            "วางแผนเกษียณอายุด้วยกองทุนสำรองเลี้ยงชีพและ RMF",
            "สร้างความมั่นคงทางการเงินหลังเกษียณตั้งแต่วันนี้",
            "วางแผนเกษียณอายุด้วยกองทุนสำรองเลี้ยงชีพและ RMF ตั้งแต่การเข้าใจกลไกและสิทธิประโยชน์ทางภาษี ไปจนถึงการคำนวณเงินที่ต้องออมต่อเดือนเพื่อชีวิตหลังเกษียณที่มั่นคง",
            CourseLevel.Intermediate, 1090m, 1790m,
            ["เข้าใจกลไกกองทุนสำรองเลี้ยงชีพและสิทธิประโยชน์ทางภาษีของ RMF", "คำนวณเงินที่ต้องการใช้หลังเกษียณและเงินที่ต้องออมต่อเดือน", "เลือกแผนการลงทุนในกองทุนสำรองเลี้ยงชีพให้เหมาะกับอายุ"],
            ["ไม่จำเป็นต้องมีพื้นฐานการลงทุนมาก่อน"],
            [
                new CourseSectionSeedSpec("เข้าใจเครื่องมือวางแผนเกษียณ",
                [
                    new CourseEpisodeSeedSpec("กองทุนสำรองเลี้ยงชีพทำงานอย่างไร", 720),
                    new CourseEpisodeSeedSpec("RMF คืออะไร ได้สิทธิประโยชน์ทางภาษีอย่างไร", 660),
                ]),
                new CourseSectionSeedSpec("วางแผนเกษียณของตัวเอง",
                [
                    new CourseEpisodeSeedSpec("คำนวณเงินที่ต้องออมต่อเดือนเพื่อเกษียณ", 900),
                    new CourseEpisodeSeedSpec("เลือกแผนการลงทุนในกองทุนสำรองเลี้ยงชีพให้เหมาะกับอายุ", 840),
                ]),
            ]),
    ];
}
