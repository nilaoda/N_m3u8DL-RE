# N_m3u8DL-RE
跨平台的DASH/HLS下载工具。支持点播、直播。

---
Beta Version：

Find latest binary at [Actions](https://github.com/nilaoda/N_m3u8DL-RE/actions)

测试版本下载：

在这里查看最新可执行文件 [Actions](https://github.com/nilaoda/N_m3u8DL-RE/actions)

登录GitHub账号后，点进最新有绿色对勾✅的一项，翻到最下面看到Artifacts，点击下载

版本较低的Windows系统自带的终端可能不支持本程序，替代方案：在 [cmder](https://github.com/cmderdev/cmder) 中运行。

---

# 运行截图

## 点播

![RE1](img/RE.gif)

还可以并行下载+自动混流


![RE2](img/RE2.gif)

## 直播

录制TS直播源：

[click to show gif](http://pan.iqiyi.com/file/paopao/W0LfmaMRvuA--uCdOpZ1cldM5JCVhMfIm7KFqr4oKCz80jLn0bBb-9PWmeCFZ-qHpAaQydQ1zk-CHYT_UbRLtw.gif)

录制MPD直播源：

[click to show gif](http://pan.iqiyi.com/file/paopao/nmAV5MOh0yIyHhnxdgM_6th_p2nqrFsM4k-o3cUPwUa8Eh8QOU4uyPkLa_BlBrMa3GBnKWSk8rOaUwbsjKN14g.gif)

录制过程中，借助ffmpeg完成对音视频的实时混流
```
ffmpeg -readrate 1 -i 2022-09-21_19-54-42_V.mp4 -i 2022-09-21_19-54-42_V.chi.m4a -c copy 2022-09-21_19-54-42_V.ts
```
