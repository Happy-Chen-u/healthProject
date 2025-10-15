CREATE TABLE [dbo].[CaseManagement]
(
    [Id] INT NOT NULL IDENTITY(1,1) PRIMARY KEY, -- 自動遞增的主鍵
    [UserId] INT NOT NULL FOREIGN KEY REFERENCES [dbo].[Users](Id), -- 連結使用者
    [Name] NVARCHAR(100) NOT NULL,              -- 姓名
    [Gender] NVARCHAR(10) NOT NULL,             -- 性別 (男/女)
    [BirthDate] DATE NOT NULL,                  -- 出生日期
    [IDNumber] NVARCHAR(12) NOT NULL FOREIGN KEY REFERENCES [dbo].[Users](IDNumber), -- 身分證字號
    [Height] DECIMAL(5,2) NOT NULL,             -- 身高 (單位: 公分, 最多2位小數)
    [Weight] DECIMAL(5,2) NOT NULL,             -- 體重 (單位: 公斤, 最多2位小數)
    [BMI] BIT NOT NULL DEFAULT 0,              -- BMI 是否勾選
    [BMI_Value] DECIMAL(5,2) NULL,                            -- BMI (單位: 無，計算值可為空)
    [SystolicBP] BIT NOT NULL DEFAULT 0,       -- 收縮壓 是否勾選
    [SystolicBP_Value] DECIMAL(5,2) NULL,                     -- 收縮壓 (單位: mmHg)
    [DiastolicBP] BIT NOT NULL DEFAULT 0,      -- 舒張壓 是否勾選
    [DiastolicBP_Value] DECIMAL(5,2) NULL,                    -- 舒張壓 (單位: mmHg)
    [CurrentWaist] BIT NOT NULL DEFAULT 0,     -- 當前腰圍 是否勾選
    [CurrentWaist_Value] DECIMAL(5,1) NULL,                   -- 當前腰圍 (單位: 公分)
    [FastingGlucose] BIT NOT NULL DEFAULT 0,   -- 空腹血糖 是否勾選
    [FastingGlucose_Value] DECIMAL(5,1) NULL,                 -- 空腹血糖 (單位: mg/dL)
    [HDL] BIT NOT NULL DEFAULT 0,              -- HDL 是否勾選
    [HDL_Value] DECIMAL(6,1) NULL,                            -- HDL (單位: mg/dL)
    [Triglycerides] BIT NOT NULL DEFAULT 0,    -- 三酸甘油酯 是否勾選
    [Triglycerides_Value] DECIMAL(6,1) NULL,                  -- 三酸甘油酯 (單位: mg/dL)
    [FollowUpDate] DATE NULL,                  -- 追蹤日期 (建議回診日期)
    [Assessment] BIT NOT NULL DEFAULT 0,               -- 收案評估 是否勾選
    [AssessmentDate] DATETIME NULL,                    -- 收案評估 日期 (改為手動輸入)
    [AnnualAssessment] BIT NOT NULL DEFAULT 0,         -- 年度評估 是否勾選
    [AnnualAssessment_Date] DATETIME NULL,              -- 年度評估 日期 (改為手動輸入)

-- 運動 (單選)
    [ExerciseNone] BIT NOT NULL DEFAULT 0,         -- 無
    [ExerciseUsually] BIT NOT NULL DEFAULT 0,   -- 偶爾
    [ExerciseAlways] BIT NOT NULL DEFAULT 0,     -- 經常 (每周累計達150分鐘)
-- 抽菸 (單選)
    [SmokingNone] BIT NOT NULL DEFAULT 0,          -- 無
    [SmokingUsually] BIT NOT NULL DEFAULT 0,    -- 偶爾
    [SmokingUnder10] BIT NOT NULL DEFAULT 0,       -- 平均一天約吸10支菸以下
    [SmokingOver10] BIT NOT NULL DEFAULT 0,        -- 平均一天約吸10支菸(含)以上

-- 嚼檳榔 (單選)
    [BetelNutNone] BIT NOT NULL DEFAULT 0,         -- 無
    [BetelNutUsually] BIT NOT NULL DEFAULT 0,   -- 偶爾
    [BetelNutAlways] BIT NOT NULL DEFAULT 0,     -- 經常

-- 疾病風險 (單選)
    [CoronaryHigh] BIT NOT NULL DEFAULT 0,     -- 冠心病 - 高風險
    [CoronaryMedium] BIT NOT NULL DEFAULT 0,   -- 冠心病 - 中風險
    [CoronaryLow] BIT NOT NULL DEFAULT 0,      -- 冠心病 - 低風險
    [CoronaryNotApplicable] BIT NOT NULL DEFAULT 0,-- 冠心病 - 不適用
    [DiabetesHigh] BIT NOT NULL DEFAULT 0,     -- 糖尿病 - 高風險
    [DiabetesMedium] BIT NOT NULL DEFAULT 0,   -- 糖尿病 - 中風險
    [DiabetesLow] BIT NOT NULL DEFAULT 0,      -- 糖尿病 - 低風險
    [DiabetesNotApplicabe] BIT NOT NULL DEFAULT 0,-- 糖尿病 - 不適用
    [HypertensionHigh] BIT NOT NULL DEFAULT 0, -- 高血壓 - 高風險
    [HypertensionMedium] BIT NOT NULL DEFAULT 0,-- 高血壓 - 中風險
    [HypertensionLow] BIT NOT NULL DEFAULT 0,  -- 高血壓 - 低風險
    [HypertensionNotApplicable] BIT NOT NULL DEFAULT 0,-- 高血壓 - 不適用
    [StrokeHigh] BIT NOT NULL DEFAULT 0,       -- 腦中風 - 高風險
    [StrokeMedium] BIT NOT NULL DEFAULT 0,     -- 腦中風 - 中風險
    [StrokeLow] BIT NOT NULL DEFAULT 0,        -- 腦中風 - 低風險
    [StrokeNotApplicable] BIT NOT NULL DEFAULT 0,  -- 腦中風 - 不適用
    [CardiovascularHigh] BIT NOT NULL DEFAULT 0,-- 心血管不良事件 - 高風險
    [CardiovascularMedium] BIT NOT NULL DEFAULT 0,-- 心血管不良事件 - 中風險
    [CardiovascularLow] BIT NOT NULL DEFAULT 0,-- 心血管不良事件 - 低風險
    [CardiovascularNotApplicable] BIT NOT NULL DEFAULT 0, -- 心血管不良事件 - 不適用

-- 戒菸服務
    [SmokingService] BIT NOT NULL DEFAULT 0,     -- 戒菸（吸菸≥10支/日或尼古丁成癮度≥4分，可提供或轉介戒菸服務）
    [SmokingServiceType1] BIT NOT NULL DEFAULT 0, -- □戒菸指導（無意願接受戒菸服務或<10支/日或尼古丁成癮度<4分）
    [SmokingServiceType2] BIT NOT NULL DEFAULT 0, -- □戒菸服務（≥10支/日或尼古丁成癮度≥4分）
    [SmokingServiceType2_Provide] BIT NOT NULL DEFAULT 0,   -- □提供戒菸服務
    [SmokingServiceType2_Referral] BIT NOT NULL DEFAULT 0,  -- □同意轉介戒菸服務
    
    -- 戒檳服務
    [BetelNutService] BIT NOT NULL DEFAULT 0,    -- □戒檳
    [BetelQuitGoal] BIT NOT NULL DEFAULT 0,      -- 戒檳目標
    [BetelQuitYear] INT NULL,                    -- 戒檳目標年
    [BetelQuitMonth] INT NULL,                   -- 戒檳目標月
    [BetelQuitDay] INT NULL,                     -- 戒檳目標日
    [OralExam] BIT NOT NULL DEFAULT 0,            -- 安排口腔黏膜檢查
    [OralExamYear] INT NULL,                     -- 安排口腔黏膜檢查年
    [OralExamMonth] INT NULL,                    -- 安排口腔黏膜檢查月
    
    -- 飲食管理
    [DietManagement] BIT NOT NULL DEFAULT 0,     -- □每日建議攝取熱量
    [DailyCalories1200] BIT NOT NULL DEFAULT 0,  -- □1200大卡
    [DailyCalories1500] BIT NOT NULL DEFAULT 0,  -- □1500大卡
    [DailyCalories1800] BIT NOT NULL DEFAULT 0,  -- □1800大卡
    [DailyCalories2000] BIT NOT NULL DEFAULT 0,  -- □2000大卡
    [DailyCaloriesOther] BIT NOT NULL DEFAULT 0, -- □其他
    [DailyCaloriesOtherValue] NVARCHAR(50) NULL, -- 其他數值填寫
    [ReduceFriedFood] BIT NOT NULL DEFAULT 0,    -- □減少油炸物
    [ReduceSweetFood] BIT NOT NULL DEFAULT 0,    -- □減少甜食
    [ReduceSalt] BIT NOT NULL DEFAULT 0,         -- □減少鹽
    [ReduceSugaryDrinks] BIT NOT NULL DEFAULT 0, -- □減少含糖飲料
    [ReduceOther] BIT NOT NULL DEFAULT 0,        -- □其他
    [ReduceOtherValue] NVARCHAR(100) NULL,       -- 其他數值填寫

-- 想達成的腰圍體重
[Achievement] BIT NOT NULL DEFAULT 0,   -- 想達成(主選項)
[WaistTarget_Value] DECIMAL(5,1) NULL,             -- 想達成的腰圍 (單位: 公分)
[WeightTarget_Value] DECIMAL(5,1) NULL,            -- 想達成的體重 (單位: 公斤)

--量血壓
[BloodPressureGuidance722] BIT NOT NULL DEFAULT 0, -- 量血壓 - 指導722量測

--運動建議
[ExerciseRecommendation] BIT NOT NULL DEFAULT 0,   -- 運動建議 (主選項)
[ExerciseGuidance] BIT NOT NULL DEFAULT 0,         -- 提供運動指導
[SocialExerciseResources] BIT NOT NULL DEFAULT 0,  -- 提供社會運動資源
 [SocialExerciseResources_Text] NVARCHAR(500) NULL,          -- 社會運動資源描述
-- 其他叮嚀 
    [OtherReminders] BIT NOT NULL DEFAULT 0,   -- 其他叮嚀 (主選項)
    [FastingGlucoseTarget] BIT NOT NULL DEFAULT 0,         -- 飯前血糖個人目標值
    [HbA1cTarget] BIT NOT NULL DEFAULT 0,                  -- 醣化血紅素個人目標值
    [HbA1cTarget_Value] DECIMAL(5,1) NULL,                          -- 醣化血紅素目標值 (mg/dL 或 %)
    [TriglyceridesTarget] BIT NOT NULL DEFAULT 0,          -- 三酸甘油酯個人目標值
    [TriglyceridesTarget_Value] DECIMAL(6,1) NULL,                  -- 三酸甘油酯目標值 (mg/dL)
    [HDL_CholesterolTarget] BIT NOT NULL DEFAULT 0,     -- 高密度脂蛋白膽固醇個人目標值
    [LDL_CholesterolTarget] BIT NOT NULL DEFAULT 0,     -- 低密度脂蛋白膽固醇個人目標值
    [LDL_CholesterolTarget_Value] DECIMAL(6,1) NULL,              -- 低密度脂蛋白膽固醇目標值 (mg/dL)
)
