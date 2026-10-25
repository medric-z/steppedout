namespace SteppedOut;

// How a language says a length of time. The lines themselves are in tools/text; these are the rules
// for putting a number in them.
//
// brief: English "1 hr 20 min" and "45 sec" in the status box, the tray and the notes, against
// "1 hour 20 minutes" in the lists. Arabic always uses full words.
// standalone: Arabic only. A time that stands alone, as a list item does, or that follows "+ " in
// the why line, takes the nominative two-form, ساعتان and دقيقتان; one that follows a word such as
// بعد or لمدة takes ساعتين and دقيقتين.
abstract class V1Words
{
    public static readonly V1Words English = new EnglishWords();
    public static readonly V1Words Arabic = new ArabicWords();

    // Hours and minutes together; zero of both reads as "0 min".
    public abstract string Time(int hours, int minutes, bool brief, bool standalone);

    public abstract string Seconds(int seconds, bool brief, bool standalone);

    // Whole days, as a pause says them: "7 days"; Arabic يومان، 3 أيام، 11 يومًا.
    public abstract string Days(int days, bool standalone);

    sealed class EnglishWords : V1Words
    {
        public override string Time(int hours, int minutes, bool brief, bool standalone)
        {
            string h = brief ? $"{hours} hr" : hours == 1 ? "1 hour" : $"{hours} hours";
            string m = brief ? $"{minutes} min" : minutes == 1 ? "1 minute" : $"{minutes} minutes";
            if (hours == 0) return m;
            return minutes == 0 ? h : h + " " + m;
        }

        public override string Seconds(int seconds, bool brief, bool standalone) =>
            brief ? $"{seconds} sec" : seconds == 1 ? "1 second" : $"{seconds} seconds";

        public override string Days(int days, bool standalone) => days == 1 ? "1 day" : $"{days} days";
    }

    sealed class ArabicWords : V1Words
    {
        public override string Time(int hours, int minutes, bool brief, bool standalone)
        {
            string h = Counted(hours, "ساعة", standalone ? "ساعتان" : "ساعتين", "ساعات", "ساعة");
            string m = Counted(minutes, "دقيقة", standalone ? "دقيقتان" : "دقيقتين", "دقائق", "دقيقة");
            if (hours == 0) return m;
            return minutes == 0 ? h : h + " و" + m;
        }

        public override string Seconds(int seconds, bool brief, bool standalone) =>
            Counted(seconds, "ثانية", standalone ? "ثانيتان" : "ثانيتين", "ثوانٍ", "ثانية");

        // يوم is masculine, so 11 and up take the accusative يومًا.
        public override string Days(int days, bool standalone) =>
            Counted(days, "يوم", standalone ? "يومان" : "يومين", "أيام", "يومًا");

        // One and two are said by the word alone (ساعة، ساعتين). After that the number comes
        // first: 3 to 10 take the plural and 11 up the singular, by the last two digits, so 103
        // takes the plural and 111 the singular, as Arabic counts them.
        static string Counted(int n, string one, string two, string few, string many) => n switch
        {
            1 => one,
            2 => two,
            _ when n % 100 is >= 3 and <= 10 => $"{n} {few}",
            _ => $"{n} {many}",
        };
    }
}
