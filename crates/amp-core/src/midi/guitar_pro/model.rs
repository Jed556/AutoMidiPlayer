use crate::input::chord_wheel::StrumDirection;

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct GuitarProBrush {
    pub time_us: u64,
    pub track_index: usize,
    pub direction: StrumDirection,
    /// 6-bit string mask ordered high-to-low (String 1 = bit 0).
    pub strings: u8,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct GuitarProChordMark {
    pub time_us: u64,
    pub track_index: usize,
    pub name: String,
    pub capo: i32,
}

#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct GuitarProMeta {
    pub artist: Option<String>,
    pub album: Option<String>,
    pub brushes: Vec<GuitarProBrush>,
    pub chord_marks: Vec<GuitarProChordMark>,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Default)]
pub(super) enum GpTripletFeel {
    #[default]
    None,
    Triplet8th,
    Triplet16th,
    Dotted8th,
    Dotted16th,
    Scottish8th,
    Scottish16th,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub(super) enum GpDirection {
    TargetCoda,
    TargetDoubleCoda,
    TargetSegno,
    TargetSegnoSegno,
    TargetFine,
    JumpDaCapo,
    JumpDaCapoAlCoda,
    JumpDaCapoAlDoubleCoda,
    JumpDaCapoAlFine,
    JumpDalSegno,
    JumpDalSegnoAlCoda,
    JumpDalSegnoAlDoubleCoda,
    JumpDalSegnoAlFine,
    JumpDalSegnoSegno,
    JumpDalSegnoSegnoAlCoda,
    JumpDalSegnoSegnoAlDoubleCoda,
    JumpDalSegnoSegnoAlFine,
    JumpDaCoda,
    JumpDaDoubleCoda,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Default)]
pub(super) enum GpDuration {
    Long,
    DoubleWhole,
    Whole,
    Half,
    #[default]
    Quarter,
    Eighth,
    Sixteenth,
    ThirtySecond,
    SixtyFourth,
    OneHundredTwentyEighth,
    TwoHundredFiftySixth,
}

impl GpDuration {
    /// Quarter-note rational numerator/denominator before dots or tuplets.
    pub fn quarter_fraction(self) -> Frac {
        match self {
            Self::Long => Frac::new(16, 1),
            Self::DoubleWhole => Frac::new(8, 1),
            Self::Whole => Frac::new(4, 1),
            Self::Half => Frac::new(2, 1),
            Self::Quarter => Frac::new(1, 1),
            Self::Eighth => Frac::new(1, 2),
            Self::Sixteenth => Frac::new(1, 4),
            Self::ThirtySecond => Frac::new(1, 8),
            Self::SixtyFourth => Frac::new(1, 16),
            Self::OneHundredTwentyEighth => Frac::new(1, 32),
            Self::TwoHundredFiftySixth => Frac::new(1, 64),
        }
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Default)]
pub(super) enum GpGraceType {
    #[default]
    BeforeBeat,
    OnBeat,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub(super) struct GpBendPoint {
    /// Horizontal beat ratio mapped onto 0..=60 (alphaTab convention).
    pub offset: u16,
    /// Quarter-tones (2 = +1 semitone, 4 = +2 semitones).
    pub value: i16,
}

#[derive(Debug, Clone, Default)]
pub(super) struct GpTempoAutomation {
    pub ratio_position: f64,
    pub bpm: f64,
}

#[derive(Debug, Clone, Default)]
pub(super) struct GpMasterBar {
    pub num: i32,
    pub den: i32,
    pub is_anacrusis: bool,
    pub is_free_time: bool,
    pub is_repeat_start: bool,
    pub repeat_count: i32,
    pub alternate_endings: u32,
    pub triplet_feel: GpTripletFeel,
    pub key_sig: Option<(i8, bool)>,
    pub tempo_automations: Vec<GpTempoAutomation>,
    pub directions: Vec<GpDirection>,
}

#[derive(Debug, Clone, Default)]
pub(super) struct GpNote {
    /// 0-based high-to-low string index (`0` = String 1 high E).
    pub string_idx: Option<usize>,
    pub fret: i32,
    pub octave: i32,
    pub tone: i32,
    pub percussion_articulation: Option<i32>,
    pub velocity: u8,
    pub duration_percent: f64,
    pub is_dead: bool,
    pub is_ghost: bool,
    pub is_tie_destination: bool,
    pub bends: Vec<GpBendPoint>,
}

#[derive(Debug, Clone)]
pub(super) struct GpBeat {
    pub is_empty: bool,
    pub duration: GpDuration,
    pub dots: i32,
    pub tuplet_num: i32,
    pub tuplet_den: i32,
    pub grace_type: Option<GpGraceType>,
    pub brush: Option<StrumDirection>,
    pub chord_name: Option<String>,
    pub tempo_change: Option<f64>,
    pub program_change: Option<u8>,
    pub volume_change: Option<u8>,
    pub balance_change: Option<u8>,
    pub notes: Vec<GpNote>,
}

impl Default for GpBeat {
    fn default() -> Self {
        Self {
            is_empty: false,
            duration: GpDuration::Quarter,
            dots: 0,
            tuplet_num: -1,
            tuplet_den: -1,
            grace_type: None,
            brush: None,
            chord_name: None,
            tempo_change: None,
            program_change: None,
            volume_change: None,
            balance_change: None,
            notes: Vec::new(),
        }
    }
}

impl GpBeat {
    /// Duration in quarter-note beats, accounting for dots and tuplets.
    pub fn duration_beats(&self) -> Frac {
        let mut dur = self.duration.quarter_fraction();
        if self.dots == 1 {
            dur = dur.mul(Frac::new(3, 2));
        } else if self.dots == 2 {
            dur = dur.mul(Frac::new(7, 4));
        } else if self.dots > 2 && self.dots <= 6 {
            let p = 1i128 << self.dots;
            dur = dur.mul(Frac::new(p * 2 - 1, p));
        }
        if self.tuplet_num > 0 && self.tuplet_den > 0 {
            dur = dur.mul(Frac::new(i128::from(self.tuplet_den), i128::from(self.tuplet_num)));
        }
        dur
    }
}

#[derive(Debug, Clone, Default)]
pub(super) struct GpVoice {
    pub beats: Vec<GpBeat>,
}

#[derive(Debug, Clone, Default)]
pub(super) struct GpBar {
    pub voices: Vec<GpVoice>,
}

#[derive(Debug, Clone, Default)]
pub(super) struct GpStaff {
    pub is_percussion: bool,
    /// Open string MIDI pitches ordered String 1 (high) -> String N (low).
    pub tuning: Vec<i32>,
    pub capo: i32,
    pub bars: Vec<GpBar>,
}

#[derive(Debug, Clone, Default)]
pub(super) struct GpArticulation {
    pub id: i32,
    pub output_midi: i32,
}

#[derive(Debug, Clone, Default)]
pub(super) struct GpTrack {
    pub name: String,
    pub program: u8,
    pub volume: u8,
    pub balance: u8,
    pub is_mute: bool,
    pub is_solo: bool,
    pub staves: Vec<GpStaff>,
    pub percussion_articulations: Vec<GpArticulation>,
}

#[derive(Debug, Clone)]
pub(super) struct GpScore {
    pub title: String,
    pub artist: String,
    pub album: String,
    pub tempo_bpm: f64,
    pub master_bars: Vec<GpMasterBar>,
    pub tracks: Vec<GpTrack>,
}

impl Default for GpScore {
    fn default() -> Self {
        Self {
            title: String::new(),
            artist: String::new(),
            album: String::new(),
            tempo_bpm: 120.0,
            master_bars: Vec::new(),
            tracks: Vec::new(),
        }
    }
}

/// Exact reduced rational number (in quarter-note beats unless stated otherwise).
#[derive(Debug, Clone, Copy, PartialEq, Eq, PartialOrd, Ord)]
pub(super) struct Frac {
    pub num: i128,
    pub den: i128,
}

impl Frac {
    pub const ZERO: Self = Self { num: 0, den: 1 };

    pub fn new(num: i128, den: i128) -> Self {
        assert!(den != 0, "rational denominator cannot be zero");
        let (num, den) = if den < 0 { (-num, -den) } else { (num, den) };
        let g = gcd(num.abs(), den);
        Self {
            num: num / g,
            den: den / g,
        }
    }

    pub fn from_f64_approx(val: f64) -> Self {
        if !val.is_finite() {
            return Self::ZERO;
        }
        let scale = 10_000i128;
        Self::new((val * scale as f64).round() as i128, scale)
    }

    pub fn add(self, other: Self) -> Self {
        let g = gcd(self.den, other.den);
        let lcm_den = (self.den / g) * other.den;
        let num = self.num * (other.den / g) + other.num * (self.den / g);
        Self::new(num, lcm_den)
    }

    pub fn sub(self, other: Self) -> Self {
        self.add(Self {
            num: -other.num,
            den: other.den,
        })
    }

    pub fn mul(self, other: Self) -> Self {
        let g1 = gcd(self.num.abs(), other.den);
        let g2 = gcd(other.num.abs(), self.den);
        Self::new((self.num / g1) * (other.num / g2), (self.den / g2) * (other.den / g1))
    }

    pub fn cmp_val(self, other: Self) -> std::cmp::Ordering {
        (self.num * other.den).cmp(&(other.num * self.den))
    }
}

fn gcd(mut a: i128, mut b: i128) -> i128 {
    while b != 0 {
        let r = a % b;
        a = b;
        b = r;
    }
    a.max(1)
}
