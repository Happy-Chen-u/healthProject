CREATE TABLE [dbo].[Users]
(
    [Id] INT NOT NULL IDENTITY(1,1) PRIMARY KEY,         -- 自動遞增的主鍵
     [IDNumber] NVARCHAR(12) NOT NULL UNIQUE,             -- 身分證字號，唯一性確保不重複
    [Username] NVARCHAR(50) NOT NULL UNIQUE,             -- 帳號，唯一性確保不重複
    [PasswordHash] NVARCHAR(255) NOT NULL,               -- 密碼哈希值（加密後儲存）
    [Role] NVARCHAR(20) NOT NULL,                       -- 角色 (例如 'Patient' 或 'Admin')
    [FullName] NVARCHAR(100) NULL,                       -- 姓名 (可選)
    [CreatedDate] DATETIME DEFAULT GETDATE(),           -- 建立時間
    [IsActive] BIT DEFAULT 1                            -- 是否啟用 (1: 啟用, 0: 停用)
)