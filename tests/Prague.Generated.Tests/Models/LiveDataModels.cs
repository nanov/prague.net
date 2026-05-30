namespace TestModels;

using MessagePack;

public class OldBaseTelemetry {
	public virtual DeviceType DeviceKind { get; set; }
	public List<PeriodReading>? PeriodScore { get; set; }
	public List<ReadingSample> ReadingSamples { get; set; }
	public string SourceUrl { get; set; }
	public int PrimaryValue { get; set; } = 0;
	public int SecondaryValue { get; set; } = 0;

	public ReadingPhase? PeriodId { get; set; }

	public string? Minute { get; set; }
	public string? PeriodDisplayName { get; set; }

	public string StoppageTimeAnnounced { get; set; }
	public string StoppageTime { get; set; }

	public bool IncludeTimeInCurrentPeriod { get; set; }
	public virtual bool IsTimeBased { get; set; }
	public virtual bool ScoresIsSetBased { get; set; }
}

public class OldSensorTelemetry : OldBaseTelemetry {
	public override DeviceType DeviceKind { get; set; } = DeviceType.Sensor;

	public string? ActiveNode { get; set; }
	public int? SecondaryReadingValue { get; set; }
	public int? PrimaryReadingValue { get; set; }

	public override bool ScoresIsSetBased { get; set; } = true;
}

public enum DeviceType : short {
	Sensor = 1,
	Gauge = 2
}

[MessagePackObject]
public class ReadingSample {
	[Key(0)] public int? SecondaryValue { get; set; }

	[Key(1)] public int? PrimaryValue { get; set; }

	[Key(2)] public ReadingPhase PeriodId { get; set; }
}

[MessagePackObject]
public class PeriodReading {
	[Key(0)] public string? PeriodNumber { get; set; }

	[Key(1)] public int? PeriodType { get; set; }

	[Key(2)] public string? PeriodDisplayName { get; set; }

	[Key(3)] public ReadingPhase PeriodId { get; set; }
}

public enum ReadingStatus : short {
	Unknown = -1,
	Draft = 0,
	Active = 1,
	Closed = 2,
	Canceled = 3,
	Interrupted = 4,
	Void = 5,
	AwaitingActive = 6
}

public enum ReadingPhase : short {
	Phase0 = 0,
	Phase1 = 1,
	Phase2 = 2,
	Phase3 = 3,
	Phase4 = 4,
	Phase5 = 5,
	Phase6 = 6,
	Phase7 = 7,
	Phase8 = 8,
	Phase9 = 9,
	Phase10 = 10,
	Phase11 = 11,
	Phase12 = 12,
	Phase13 = 13,
	Phase14 = 14,
	Phase15 = 15,
	Phase16 = 16,
	Phase17 = 17,
	Phase18 = 18,
	Phase19 = 19,
	Phase20 = 20,
	Phase21 = 21,
	Phase22 = 22,
	Phase30 = 30,
	Phase31 = 31,
	Phase32 = 32,
	Phase33 = 33,
	Phase34 = 34,
	Phase40 = 40,
	Phase41 = 41,
	Phase42 = 42,
	Phase50 = 50,
	Phase51 = 51,
	Phase52 = 52,
	Phase60 = 60,
	Phase61 = 61,
	Phase70 = 70,
	Phase71 = 71,
	Phase72 = 72,
	Phase73 = 73,
	Phase74 = 74,
	Phase75 = 75,
	Phase76 = 76,
	Phase77 = 77,
	Phase80 = 80,
	Phase81 = 81,
	Phase90 = 90,
	Phase91 = 91,
	Phase92 = 92,
	Phase93 = 93,
	Phase94 = 94,
	Phase95 = 95,
	Phase96 = 96,
	Phase97 = 97,
	Phase98 = 98,
	Phase99 = 99,
	Phase100 = 100,
	Phase110 = 110,
	Phase111 = 111,
	Phase120 = 120,
	Phase130 = 130,
	Phase140 = 140,
	Phase141 = 141,
	Phase142 = 142,
	Phase143 = 143,
	Phase144 = 144,
	Phase145 = 145,
	Phase146 = 146,
	Phase147 = 147,
	Phase151 = 151,
	Phase152 = 152,
	Phase153 = 153,
	Phase154 = 154,
	Phase155 = 155,
	Phase161 = 161,
	Phase162 = 162,
	Phase163 = 163,
	Phase164 = 164,
	Phase165 = 165,
	Phase166 = 166,
	Phase167 = 167,
	Phase168 = 168,
	Phase169 = 169,
	Phase170 = 170,
	Phase171 = 171,
	Phase301 = 301,
	Phase302 = 302,
	Phase303 = 303,
	Phase304 = 304,
	Phase305 = 305,
	Phase306 = 306,
	Phase401 = 401,
	Phase402 = 402,
	Phase403 = 403,
	Phase404 = 404,
	Phase405 = 405,
	Phase406 = 406,
	Phase407 = 407,
	Phase408 = 408,
	Phase409 = 409,
	Phase410 = 410,
	Phase411 = 411,
	Phase412 = 412,
	Phase413 = 413,
	Phase414 = 414,
	Phase415 = 415,
	Phase416 = 416,
	Phase417 = 417,
	Phase418 = 418,
	Phase419 = 419,
	Phase420 = 420,
	Phase421 = 421,
	Phase422 = 422,
	Phase423 = 423,
	Phase424 = 424,
	Phase425 = 425,
	Phase426 = 426,
	Phase427 = 427,
	Phase428 = 428,
	Phase429 = 429,
	Phase430 = 430,
	Phase431 = 431,
	Phase432 = 432,
	Phase433 = 433,
	Phase434 = 434,
	Phase435 = 435,
	Phase436 = 436,
	Phase437 = 437,
	Phase438 = 438,
	Phase439 = 439,
	Phase440 = 440,
	Phase441 = 441,
	Phase442 = 442,
	Phase443 = 443,
	Phase444 = 444,
	Phase445 = 445,
	Phase501 = 501,
	Phase502 = 502,
	Phase503 = 503,
	Phase504 = 504,
	Phase505 = 505,
	Phase506 = 506,
	Phase507 = 507,
	Phase508 = 508,
	Phase509 = 509,
	Phase510 = 510,
	Phase511 = 511,
	Phase512 = 512,
	Phase513 = 513,
	Phase514 = 514,
	Phase515 = 515,
	Phase516 = 516,
	Phase517 = 517,
	Phase518 = 518,
	Phase519 = 519,
	Phase520 = 520,
	Phase521 = 521,
	Phase522 = 522,
	Phase523 = 523,
	Phase524 = 524,
	Phase525 = 525,
	Phase526 = 526,
	Phase531 = 531,
	Phase532 = 532,
	Phase533 = 533,
	Phase534 = 534,
	Phase535 = 535,
	Phase536 = 536,
	Phase537 = 537,
	Phase538 = 538,
	Phase539 = 539,
	Phase540 = 540,
	Phase541 = 541,
	Phase542 = 542,
	Phase543 = 543,
	Phase544 = 544,
	Phase545 = 545,
	Phase546 = 546,
	Phase547 = 547,
	Phase548 = 548
}
