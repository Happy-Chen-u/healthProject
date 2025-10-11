CREATE TABLE [dbo].[Today]
(
    [Id] INT NOT NULL IDENTITY(1,1) PRIMARY KEY, -- 自動遞增的主鍵
    [UserId] INT NOT NULL FOREIGN KEY REFERENCES [dbo].[Users](Id), -- 連結使用者
    [RecordDate] DATETIME DEFAULT GETDATE(),     -- 記錄日期，默認當前時間
    [ExerciseType] NVARCHAR(100) NULL,           -- 運動種類
    [ExerciseDuration] DECIMAL(5,1) NULL,        -- 運動時間 (單位: 分鐘)
    [WaterIntake] DECIMAL(6,0) NULL,             -- 水分攝取 (單位: ml)
    [Beverage] NVARCHAR(200) NULL,               -- 飲料 (文字描述)
    [Meals] NVARCHAR(500) NULL,                  -- 三餐 (文字描述)
    [Cigarettes] DECIMAL(5,0) NULL,              -- 抽菸支數
    [BetelNut] DECIMAL(5,0) NULL,                -- 嚼檳榔次數
    [BloodSugar] DECIMAL(5,1) NULL,              -- 血糖 (單位: mg/dL)
    [SystolicBP] DECIMAL(5,2) NULL,              -- 血壓 - 收縮壓 (單位: mmHg)
    [DiastolicBP] DECIMAL(5,2) NULL              -- 血壓 - 舒張壓 (單位: mmHg)
)