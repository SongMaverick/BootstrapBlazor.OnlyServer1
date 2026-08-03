# 证书生成操作记录 — 2026-07-15

> **目的**:把今天这次"TCID 政务 CA 申请下来的证书合并成 pfx 给 Kestrel 用"的全过程记下来,以后同样需求能照抄。
> 
> **注意**:本文档**不含**私钥内容、不含密码,只记命令和踩坑。

---

## 1. 背景

证书是政务 CA(TCID 类)网站申请下来的,流程决定:

```
IT 同事在另一台机器跑 OpenSSL:
  ├─ 生成 .key(私钥,单独给他)
  ├─ 生成 .csr(证书申请,提交给 CA)
  └─ CA 颁发 .cer(服务器证书) + .p7b(证书链)

用户最终拿到 3 个文件:
  - .key   私钥
  - .cer   服务器证书(公钥)
  - .p7b   证书链(中间 CA + 根 CA)
```

**目标**:把这 3 个文件合并成 1 个 `.pfx`(PKCS#12,含私钥 + 证书 + 链),给 Kestrel 直接读。

---

## 2. 文件清单

工作目录:`D:\New Volume\D7_codeBase\CA\`

| 文件名                                                     | 角色         | 大小   |
| ------------------------------------------------------- | ---------- | ---- |
| `jinwad5a.ad101.siemens-energy.net.key`                 | 私钥(PEM 格式) | 4 KB |
| `SOO05DOY_jinwad5a.ad101.siemens-energy.net.cer`        | 服务器证书      | 4 KB |
| `SOO05DOY_jinwad5a.ad101.siemens-energy.net.Bundle.p7b` | 证书链(3 张证书) | 9 KB |

证书主题域名:`jinwad5a.ad101.siemens-energy.net`(企业内部域名)

---

## 3. 完整操作步骤

### 3.1 打开 Git Bash

`Win + R` → 输 `bash` **会失败**(走 cmd 找不到)。

正确做法:**开始菜单搜 "Git Bash"** → 回车。

### 3.2 进入工作目录

```bash
cd /d/D7_codeBase/CA
# 或带空格路径:cd "/d/New Volume/D7_codeBase/CA"
```

### 3.3 第一步坑:`-certfile xxx.p7b` 直接合并会失败

❌ **失败的命令**:

```bash
openssl pkcs12 -export -out server.pfx \
  -inkey jinwad5a.ad101.siemens-energy.net.key \
  -in SOO05DOY_jinwad5a.ad101.siemens-energy.net.cer \
  -certfile SOO05DOY_jinwad5a.ad101.siemens-energy.net.Bundle.p7b
```

报错:

```
Could not find extra certificates from -certfile from ...Bundle.p7b
```

**原因**:OpenSSL 把 `.p7b` 喂给 `-certfile` 时,认为里面的证书已经被 `-in` 参数的 `.cer` 包含了,找不到"额外的"证书,所以抱怨。实际是 OpenSSL 解析 PKCS#7 的兼容性问题。

### 3.4 解决方案:p7b → PEM → 合并

先把 p7b 转成 PEM(纯文本,OpenSSL 通用格式),再用 PEM 合并:

```bash
# 1) 把 .p7b 拆成 PEM(每张证书一段 -----BEGIN CERTIFICATE-----)
openssl pkcs7 -in SOO05DOY_jinwad5a.ad101.siemens-energy.net.Bundle.p7b -print_certs | sed -n '/BEGIN CERTIFICATE/,/END CERTIFICATE/p' > chain.pem

# 2) 验证 chain.pem 里有 3 张证书
grep -c "BEGIN CERTIFICATE" chain.pem
# 期望输出: 3

# 3) 生成 pfx
openssl pkcs12 -export -out server.pfx \
  -inkey jinwad5a.ad101.siemens-energy.net.key \
  -in SOO05DOY_jinwad5a.ad101.siemens-energy.net.cer \
  -certfile chain.pem
```

跑第 3 步时:

- 提示 `Enter Export Password:` → **设密码**(待会 Kestrel 读 pfx 用,记住!)
- 提示 `Verifying - Enter Export Password:` → 再输一次

完事,目录里有 `server.pfx`。

### 3.5 验证 pfx

⚠️ 同 3.6 节的坑 — `-info` 不展开证书内容,grep subject/issuer 匹配不到。

正确命令(用 `openssl pkcs12 -clcerts` 抽出证书,再 pipe 给 `openssl x509`)。

**看完整链(服务器证书 + 中间 + 根)所有 subject/issuer**:

```bash
openssl pkcs12 -in server.pfx -nokeys -passin pass:'你的密码' | openssl x509 -noout -subject -issuer | nl
```

或者按证书逐个查看:

```bash
# 抽出所有证书到 chain-all.pem
openssl pkcs12 -in server.pfx -nokeys -passin pass:'你的密码' -out chain-all.pem

# 拆分,每张单独查
awk '/BEGIN CERTIFICATE/{i++} {print > "cert-"i".pem"}' chain-all.pem

# 一张一张看
for f in cert-*.pem; do echo "=== $f ==="; openssl x509 -in $f -noout -subject -issuer; done
```

期望看到 3-4 张证书,每张的 issuer 是上一张的 subject(根 CA 自签):

```
=== cert-1.pem ===
subject=CN = jinwad5a.ad101.siemens-energy.net
issuer=CN = 中间 CA 名
=== cert-2.pem ===
subject=CN = 中间 CA 名
issuer=CN = 根 CA 名
=== cert-3.pem ===
subject=CN = 根 CA 名
issuer=CN = 根 CA 名   ← 根 CA 自己签自己
```

### 3.6 检查签名算法(⚠️ 重要)

⚠️ **`openssl pkcs12 -info` 只输出 p12 文件外层信息(MAC / 加密算法 / bag 列表),不展开证书内容**。直接 pipe 给 `grep "Signature Algorithm"` 匹配不到任何东西。

要用 `openssl x509` 展开证书后再 grep:

```bash
openssl pkcs12 -in server.pfx -nokeys -clcerts -passin pass:'你的密码' | openssl x509 -noout -text | grep "Signature Algorithm"
```

期望看到 `sha256WithRSAEncryption`(现代浏览器兼容)。
如果看到 `sha1WithRSAEncryption`,**这是个雷** — 现代浏览器已逐步不信任 SHA-1 证书,部署后会红字"不安全"。需要回 CA 重新申请 SHA-256 签名的证书。

**附加验证**:把证书完整内容 dump 出来,确认主题是你的域名、有效期没过期:

```bash
openssl pkcs12 -in server.pfx -nokeys -clcerts -passin pass:'你的密码' -out server.pem
openssl x509 -in server.pem -noout -subject -dates -issuer
# 期望:
#   subject=CN = jinwad5a.ad101.siemens-energy.net
#   notBefore=Jul 14 11:01:00 2026 GMT
#   notAfter=Jul 14 11:01:00 2027 GMT(举例)
#   issuer=CN = 中间 CA 名

---

## 4. 后续步骤(留给 Kestrel 部署)

pfx 生成后,要给 Kestrel 用还得做这些事:

1. **把 pfx 文件复制到生产服务器**(建议路径:`D:\Certs\server.pfx`)
2. **传输安全**:别走微信 / 邮件明文传(走内网共享 / SCP / U 盘)
3. **密码别写进配置文件**:
   ```powershell
   # 服务器上设环境变量(持久化)
   [Environment]::SetEnvironmentVariable(
     "Kestrel__Certificates__Default__Password",
     "你的密码",
     "Machine"
   )
```

4. **Kestrel 配置**(待补,见 `docs/deploy/kestrel-https-pfx-guide.md`,本文档完成后)

---

## 5. 踩过的坑汇总

| 坑                            | 现象                                  | 解法                                         |
| ---------------------------- | ----------------------------------- | ------------------------------------------ |
| `Win+R bash` 找不到             | "Windows cannot find 'bash'"        | 用开始菜单搜 Git Bash                            |
| `-certfile xxx.p7b` 失败       | "Could not find extra certificates" | 先 `pkcs7 -print_certs` 转 PEM               |
| 不知道私钥文件后缀是不是 `.key`          | 问"私钥是什么"                            | 私钥 = Private Key,PEM 格式常见 `.key` / `.pem`  |
| bash 历史扩展吃掉 `!`              | `bash: !TCID: event not found`      | 密码用单引号包 `pass:'你的密码'`                      |
| `openssl pkcs12 -info` 不展开证书 | `grep "Signature Algorithm"` 匹配不到   | pipe 给 `openssl x509 -text`(见 3.5 / 3.6 节) |

---

**记录日期**:2026-07-15
**记录人**:ChangKong + Claude
**适用项目**:BootstrapBlazor.OnlyServer1(Kestrel 自托管部署)
**关联 commit**:(待补)