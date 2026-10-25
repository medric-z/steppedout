namespace SteppedOut;

static class Arabic
{
    // Durations always come after a preposition or as an object ("بعد"، "لمدة"، "أمضيت"), so the
    // two-forms دقيقتين and ساعتين are in the right case wherever they appear. The word after a
    // number typed into a box carries on the sentence before it ("لمدة"، "بعد"), so it takes دقيقتين
    // like the durations.
    public static readonly Strings Text = new()
    {
        V1 = ArabicV1.Text,
        RightToLeft = true,
        SecondsCount = n => Counted(n, "ثانية", "ثانيتين", "ثوانٍ", "ثانية"),
        MinutesCount = n => Counted(n, "دقيقة", "دقيقتين", "دقائق", "دقيقة"),
        HoursCount = n => Counted(n, "ساعة", "ساعتين", "ساعات", "ساعة"),
        MinutesWord = n => Noun(n, "دقيقة", "دقيقتين", "دقائق", "دقيقة"),
        And = " و",

        WelcomeTitle = "مرحبًا بك في SteppedOut",
        WelcomeWhere = "تظهر أيقونة القفل بجوار الساعة. إذا كانت مخفية، فانقر على السهم ^ ثم اسحبها إلى شريط المهام. انقر عليها لفتح الإعدادات، وانقر بزر الماوس الأيمن لفتح القائمة.",
        Done = "تم",
        OpenSettingsButton = "فتح الإعدادات",

        AlreadyRunning = "يعمل SteppedOut بالفعل.",

        ConfigNotUsed = "تعذر قراءة ملف config.json",
        ConfigDefaultsUntilFixed = "تُستخدم الإعدادات الافتراضية حتى تصلحه. يوضح السجل في المجلد نفسه السبب.",
        ConfigEarlierUntilFixed = "يستمر استخدام الإعدادات السابقة حتى تصلحه. يوضح السجل في المجلد نفسه السبب.",
        ConfigSkipped = "تم تجاهل جزء من ملف config.json",
        ConfigSkippedDetail = "فيه اسم إعداد غير معروف، ربما بسبب خطأ في الكتابة. تم استخدام باقي الملف، ويذكره السجل في المجلد نفسه.",

        MinutesFieldName = "{0}، بالدقائق",
        MinutesRange = "من 1 إلى 1440 دقيقة",
        MinutesThatIs = "المدة: {0}",
        MinutesClamped = "اكتب رقمًا من 1 إلى 1440 دقيقة. القيمة المستخدمة الآن: {0}.",
        MinutesNotANumber = "اكتب عدد الدقائق. القيمة المستخدمة الآن: {0}.",

        OpenAction = "فتح",
        SelectAction = "تحديد",
        // تفعيل, not تشغيل: إعادة التشغيل is what Windows calls a restart.
        TurnOn = "تفعيل",
        TurnOff = "إيقاف",
    };

    // Arabic counts: one and two have their own forms, 3 to 10 take the plural, 11 and up the
    // singular again.
    static string Counted(int n, string one, string two, string few, string many) =>
        n is 1 or 2 ? Noun(n, one, two, few, many) : $"{n} {Noun(n, one, two, few, many)}";

    // The counted word alone, for where the number is already shown.
    static string Noun(int n, string one, string two, string few, string many) => n switch
    {
        1 => one,
        2 => two,
        _ when n % 100 is >= 3 and <= 10 => few,
        _ => many,
    };
}
