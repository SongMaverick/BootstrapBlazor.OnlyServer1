# BootstrapBlazor.OCCMassSetup

为 OCC 重量复制页面初始化专用角色和权限：

- 角色：`accounting`
- 权限：`OCCMass.Use`

先在 `appsettings.json` 配置数据库连接字符串，然后执行：

```powershell
dotnet run --project BootstrapBlazor.OCCMassSetup -- --check
dotnet run --project BootstrapBlazor.OCCMassSetup -- --install
dotnet run --project BootstrapBlazor.OCCMassSetup -- --install --production
```

安装程序是幂等的，只会创建缺少的角色、权限和角色权限关联，不会删除或覆盖已有数据。
