CREATE TABLE [dbo].[CaseManagement]
(
    [Id] INT NOT NULL IDENTITY(1,1) PRIMARY KEY, -- 自動遞增的主鍵
    [UserId] INT NOT NULL FOREIGN KEY REFERENCES [dbo].[Users](Id), -- 連結使用者
    [Name] NVARCHAR(100) NOT NULL,              -- 姓名
    [Gender] NVARCHAR(10) NOT NULL,             -- 性別 (男/女)
    [BirthDate] DATE NOT NULL,                  -- 出生日期
    [Height] DECIMAL(5,2) NOT NULL,             -- 身高 (單位: 公分, 最多2位小數)
    [Weight] DECIMAL(5,2) NOT NULL,             -- 體重 (單位: 公斤, 最多2位小數)
    [BMI] DECIMAL(5,2) NULL,                    -- BMI (可為空)
    [SystolicBP] DECIMAL(5,2) NULL,             -- 收縮壓 (單位: mmHg)
    [DiastolicBP] DECIMAL(5,2) NULL,            -- 舒張壓 (單位: mmHg)
    
    [FastingGlucose] DECIMAL(5,1) NULL,         -- 空腹血糖 (單位: mg/dL)
    [HbA1c] DECIMAL(5,1) NULL,                  -- HbA1c (單位: %)
    [HDL] DECIMAL(6,1) NULL,                    -- HDL (單位: mg/dL)
    [Triglycerides] DECIMAL(6,1) NULL,          -- 三酸甘油酯 (單位: mg/dL)
    [FollowUpDate] DATE NULL,                  -- 追蹤日期 (可為空)
    [CreatedDate] DATETIME DEFAULT GETDATE()   -- 建立時間 (自動填入當前時間)
)