namespace LevelLibrary;

/// <summary>多语言词条表 · 第五批：去特效（界面骨架 / 三个主分类 / 子分组 / 全部开关 / 四套预设）
/// 与主界面大厅、设置页、登录页、更新器的补漏。
/// 去特效的英文一律照 ychfx 原 mod 的 `Languages/en_US.lang`（去掉 Override / Disable 前缀取核心名），
/// 换过来的老玩家看到的是同一套术语；韩文按 ADOFAI 韩语社区的习惯说法翻。
/// 键必须与源码里逐字一致（全角括号、引号都算）；查不到就原样显示中文。</summary>
public static class LangData5 {
    /// <summary>注册第五批词条（由 `LangData` 的静态构造器显式调用一次，本类自己的静态构造器没人碰）。</summary>
    public static void Batch11() {
        Lang.Add("去特效", "Effect Remover", "이펙트 제거");
        Lang.Add("单独去特效", "Enable Effect Remover", "이펙트 제거 사용");
        Lang.Add("预设（可多选，叠加生效）", "Presets (multi-select, stackable)", "프리셋 (다중 선택, 중첩 적용)");
        Lang.Add("详细去特效", "Advanced Effect Remover", "상세 이펙트 제거");
        Lang.Add("当前没有开启任何去特效项", "No effect removal is enabled", "켜진 이펙트 제거 항목이 없습니다");
        Lang.Add("全局去特效（默认关闭）", "Global Effect Remover (off by default)", "전역 이펙트 제거 (기본 꺼짐)");
        Lang.Add("打开详细设置", "Open advanced settings", "상세 설정 열기");
        Lang.Add("全局", "Global", "전역");
        Lang.Add("谱面设置", "Level Settings", "레벨 설정");
        Lang.Add("装饰物", "Decorations", "장식물");
        Lang.Add("事件", "Events", "이벤트");
        Lang.Add("音乐", "Song", "음악");
        Lang.Add("轨道", "Track", "트랙");
        Lang.Add("镜头", "Camera", "카메라");
        Lang.Add("杂项", "Misc", "기타");
        Lang.Add("听觉事件", "Auditory", "청각");
        Lang.Add("星球事件", "Planet", "행성");
        Lang.Add("轨道事件", "Track Events", "트랙 이벤트");
        Lang.Add("装饰事件", "Deco Events", "장식 이벤트");
        Lang.Add("视觉杂项", "Visual", "시각");
        Lang.Add("特效事件", "Effects", "이펙트");
        Lang.Add("其他事件", "Misc Events", "기타 이벤트");
        Lang.Add("去除装饰物相关", "Remove Decorations", "장식물 제거");
        Lang.Add("去除摄像头相关", "Remove Cameras", "카메라 제거");
        Lang.Add("去除滤镜相关", "Remove Filters", "필터 제거");
        Lang.Add("去除轨道相关", "Remove Tracks", "트랙 제거");
        Lang.Add("打拍声", "Hitsound", "히트사운드");
        Lang.Add("打拍声音量", "Hitsound Volume", "히트사운드 볼륨");
        Lang.Add("轨道纹理", "Track texture", "트랙 텍스처");
        Lang.Add("轨道颜色类型", "Track color type", "트랙 색상 유형");
        Lang.Add("轨道风格", "Track style", "트랙 스타일");
        Lang.Add("轨道主色调", "Track color", "트랙 색상");
        Lang.Add("轨道副色调", "Secondary track color", "보조 트랙 색상");
        Lang.Add("色彩动画间隔", "Color animation interval", "색상 애니메이션 간격");
        Lang.Add("辉度", "Glow intensity", "발광 강도");
        Lang.Add("轨道出现动画", "Track appear animation", "트랙 등장 애니메이션");
        Lang.Add("动画前节拍", "Beats before for animation", "애니메이션 이전 비트");
        Lang.Add("轨道消失动画", "Track disappear animation", "트랙 사라짐 애니메이션");
        Lang.Add("动画后节拍", "Beats after for animation", "애니메이션 이후 비트");
        Lang.Add("背景图片", "Background image", "배경 이미지");
        Lang.Add("背景颜色", "Background color", "배경 색상");
        Lang.Add("自动方块", "Show tutorial background pattern", "튜토리얼 배경 패턴 표시");
        Lang.Add("无背景图时显示自动方块", "Show default BG if no image", "배경 이미지가 없으면 기본 패턴 표시");
        Lang.Add("自动方块颜色", "Tutorial background pattern color", "튜토리얼 배경 패턴 색상");
        Lang.Add("教程背景图形", "Tutorial background shape", "튜토리얼 배경 도형");
        Lang.Add("教程背景图形颜色", "Tutorial background shape color", "튜토리얼 배경 도형 색상");
        Lang.Add("关联到", "Relative to", "기준 대상");
        Lang.Add("位置", "Position", "위치");
        Lang.Add("角度", "Rotation", "회전");
        Lang.Add("缩放", "Zoom", "확대/축소");
        Lang.Add("触发方块时摄像头脉动", "Pulse camera at tiles", "타일 판정 시 카메라 펄스");
        Lang.Add("视频背景", "Video background", "동영상 배경");
        Lang.Add("方块图标描边", "Tile icon outlines", "타일 아이콘 외곽선");
        Lang.Add("黏性方块", "Sticky tiles", "접착 타일");
        Lang.Add("星球轨道缓速", "Planet orbit ease", "행성 궤도 이징");
        Lang.Add("轨道缓速部分", "Orbit ease parts", "궤도 이징 분할");
        Lang.Add("缓速部分行为", "Ease part behavior", "이징 분할 동작");
        Lang.Add("默认文本颜色", "Default text color", "기본 텍스트 색상");
        Lang.Add("默认文本阴影颜色", "Default text shadow color", "기본 텍스트 그림자 색상");
        Lang.Add("“庆贺！”文本", "\"Congratulations!\" text", "\"축하합니다!\" 텍스트");
        Lang.Add("“完美无瑕！”文本", "\"Pure Perfect!\" text", "\"퓨어 퍼펙트!\" 텍스트");
        Lang.Add("死亡图像装饰", "Killer Image Decorations", "킬러 이미지 장식");
        Lang.Add("其他图像装饰", "Other Image Decorations", "기타 이미지 장식");
        Lang.Add("文本装饰", "Text", "텍스트");
        Lang.Add("地板对象装饰", "Floor Object", "바닥 오브젝트");
        Lang.Add("星球对象装饰", "Planet Object", "행성 오브젝트");
        Lang.Add("粒子发射器", "Particle Emitter", "파티클 이미터");
        Lang.Add("设置打拍音", "Set Hitsound", "히트사운드 설정");
        Lang.Add("播放音效", "Play Sound", "효과음 재생");
        Lang.Add("设置长按音效", "Set Hold Sound", "홀드 사운드 설정");
        Lang.Add("设置星球轨道", "Set Planet Orbit", "행성 궤도 설정");
        Lang.Add("缩放行星", "Scale Planets", "행성 크기 조절");
        Lang.Add("星球半径大小", "Planet Radius Scale", "행성 반경 크기");
        Lang.Add("隐藏判定/地板图标", "Hide Judgement/Floor Icons", "판정/바닥 아이콘 숨기기");
        Lang.Add("移动轨道", "Move Track", "트랙 이동");
        Lang.Add("位置轨道", "Position Track", "트랙 위치 지정");
        Lang.Add("设置轨道颜色", "Set Track Color", "트랙 색상 설정");
        Lang.Add("设置轨道动画", "Set Track Animation", "트랙 애니메이션 설정");
        Lang.Add("重新设置轨道颜色", "Recolor Track", "트랙 색상 재설정");
        Lang.Add("检查点", "Checkpoint", "체크포인트");
        Lang.Add("自动播放格子", "Auto Play Tiles", "타일 자동 재생");
        Lang.Add("移动装饰", "Move Decorations", "장식 이동");
        Lang.Add("设置文本", "Set Text", "텍스트 설정");
        Lang.Add("发射粒子", "Emit Particle", "파티클 방출");
        Lang.Add("设置粒子", "Set Particle", "파티클 설정");
        Lang.Add("设置对象", "Set Object", "오브젝트 설정");
        Lang.Add("设置默认文本", "Set Default Text", "기본 텍스트 설정");
        Lang.Add("移动摄像头", "Move Camera", "카메라 이동");
        Lang.Add("设置背景", "Set Background", "배경 설정");
        Lang.Add("闪光", "Flash", "플래시");
        Lang.Add("预设滤镜", "Set Filter", "필터 설정");
        Lang.Add("预设高级滤镜", "Set Filter Advanced", "고급 필터 설정");
        Lang.Add("镜厅", "Hall of Mirrors", "거울의 방");
        Lang.Add("振屏", "Shake Screen", "화면 흔들기");
        Lang.Add("绽放", "Bloom", "블룸");
        Lang.Add("平铺", "Tile Screen", "화면 타일링");
        Lang.Add("卷屏", "Scroll Screen", "화면 스크롤");
        Lang.Add("设置帧率", "Set Frame Rate", "프레임레이트 설정");
        Lang.Add("编辑器附注", "Editor Comment", "에디터 주석");
        Lang.Add("书签", "Bookmark", "북마크");
        Lang.Add("谱面总数", "Charts", "채보");
        Lang.Add("谱面总数/", "Charts/", "채보/");
        Lang.Add("历程", "History", "히스토리");
        Lang.Add("当前", "Now", "현재");
        Lang.Add("等待指示", "Awaiting orders", "지시 대기");
        Lang.Add("叠加层", "Overlay", "오버레이");
        Lang.Add("叠加层设置", "Overlay settings", "오버레이 설정");
        Lang.Add("个人信息栏", "Profile bar", "프로필 바");
        Lang.Add("肥鱼设定", "Fat Fish settings", "뚱뚱이 물고기 설정");
        Lang.Add("随机", "Random", "랜덤");
        Lang.Add("新版发布", "New Release", "신규 릴리스");
        Lang.Add("问题修复", "Bug Fixes", "버그 수정");
        Lang.Add("功能新增", "New Features", "신규 기능");
        Lang.Add("交流群：959648017", "QQ Group: 959648017", "QQ 그룹: 959648017");
        Lang.Add("神经图闪现", "Neural background flash", "뉴럴 배경 플래시");
        Lang.Add("单个导入压缩包", "Single zip import", "개별 압축 파일 가져오기");
        Lang.Add("背景压暗", "Background dim", "배경 어둡게");
        Lang.Add("选择音乐文件", "Choose music file", "음악 파일 선택");
        Lang.Add("正在检查更新", "Checking for updates", "업데이트 확인 중");
        Lang.Add("空格 / 回车也可登录", "Space / Enter to log in", "Space / Enter로 로그인");
        Lang.Add("{0} 通关", "{0} Cleared", "{0} 클리어");
        Lang.Add("通关官方关卡 {0}", "Clear the official level {0}", "공식 레벨 {0} 클리어");
        Lang.Add("世界 {0}", "World {0}", "월드 {0}");
        Lang.Add("还没打过卡：打开关卡编辑器待满 5 秒就算一次", "No check-in yet: stay in the level editor for 5 seconds to check in", "아직 출석하지 않았습니다: 레벨 에디터에서 5초 머물면 1회 출석");
    }

    /// <summary>注册第十二批词条（2026-10-24 全量补漏）：此前只写中文、没过 Tr 的界面文案
    /// 全部改为走 Tr 之后，这批把"调用了 Tr 却没注册"的词条一次补齐——
    /// 含弹窗/提示/toast、周报数据行、时长格式模板、视频背景档位、宠物与立绘提示等。
    /// 风格与本表一致：英文去"喵"、韩文用 해요체。</summary>
    public static void Batch12() {
        // ---- 更新状态行（拼接片段，key 自带换行）----
        Lang.Add("\n资源包版本：", "\nAsset pack version: ", "\n리소스 팩 버전: ");
        Lang.Add("\n状态：", "\nStatus: ", "\n상태: ");
        // ---- 时长格式模板 ----
        Lang.Add("{0}小时{1}分{2}秒", "{0}h {1}m {2}s", "{0}시간 {1}분 {2}초");
        Lang.Add("{0}分{1}秒", "{0}m {1}s", "{0}분 {1}초");
        Lang.Add("{0}秒", "{0}s", "{0}초");
        // ---- 周报数据行 ----
        Lang.Add("本周游玩次数：{0} 次", "Plays this week: {0}", "이번 주 플레이 횟수: {0}회");
        Lang.Add("本周游玩时间：{0}", "Play time this week: {0}", "이번 주 플레이 시간: {0}");
        Lang.Add("历史严判最高通关等级：{0}", "Highest strict-judge clear: {0}", "엄격 판정 최고 클리어 등급: {0}");
        Lang.Add("历史击破P级：{0}", "P-rank clears: {0}", "P등급 클리어: {0}");
        Lang.Add("历史击破G级：{0}", "G-rank clears: {0}", "G등급 클리어: {0}");
        Lang.Add("历史击破U级：{0}", "U-rank clears: {0}", "U등급 클리어: {0}");
        Lang.Add("历史游玩时长：{0}", "Total play time: {0}", "총 플레이 시간: {0}");
        // ---- 成就卡（触发次数行）与宠物 ----
        Lang.Add("触发次数：{0}　（{1} 级）", "Triggers: {0} (Lv {1})", "발동 횟수: {0} (Lv {1})");
        Lang.Add("今日游玩 {0} 分钟", "Played {0} min today", "오늘 {0}분 플레이했어요");
        Lang.Add("宠物位置已复位", "Pet position reset", "펫 위치가 초기화되었어요");
        // ---- 视频背景：四档压缩 + 提示 ----
        Lang.Add("原画质", "Source", "원본 화질");
        Lang.Add("不缩不降帧，最清晰，但加载慢、低配可能卡", "No downscale, no fps drop — sharpest, but slow to load and may lag on low-end PCs", "축소·프레임 드롭 없이 가장 선명하지만 로딩이 느리고 저사양에서 버벅일 수 있어요");
        Lang.Add("高", "High", "높음");
        Lang.Add("1080p 以内 · 30fps", "Up to 1080p · 30fps", "1080p 이하 · 30fps");
        Lang.Add("中", "Mid", "중간");
        Lang.Add("720p 以内 · 30fps", "Up to 720p · 30fps", "720p 이하 · 30fps");
        Lang.Add("低", "Low", "낮음");
        Lang.Add("540p 以内 · 24fps（默认，最省）", "Up to 540p · 24fps (default, lightest)", "540p 이하 · 24fps (기본값, 가장 가벼움)");
        Lang.Add("（当前）", " (current)", " (현재)");
        Lang.Add("正在压缩视频（{0}档，长视频要一两分钟）喵", "Compressing video ({0}, long videos take a minute or two)", "동영상 압축 중({0}, 긴 동영상은 1~2분 걸려요)");
        Lang.Add("上一次还在压缩，等它完事喵", "Previous compression still running, wait for it", "이전 압축이 아직 진행 중이에요, 끝날 때까지 기다려 주세요");
        Lang.Add("找不到游戏自带的 ffmpeg，没法压缩喵", "Can't find the game's bundled ffmpeg, no compression", "게임 내장 ffmpeg를 찾을 수 없어 압축할 수 없어요");
        Lang.Add("这个视频文件不存在喵", "This video file doesn't exist", "이 동영상 파일이 없어요");
        Lang.Add("本地还没有默认视频（用上面的「下载默认视频背景」取一份）", "No default video yet (use \"Download default video\" above to get one)", "아직 기본 동영상이 없어요 (위의 \"기본 동영상 받기\"로 받으세요)");
        Lang.Add("现在用的就是内置默认视频喵", "Already using the built-in default video", "이미 내장 기본 동영상을 사용 중이에요");
        Lang.Add("恢复默认视频失败，看日志喵", "Failed to restore default video, check the log", "기본 동영상 복원 실패, 로그를 확인해 주세요");
        Lang.Add("默认视频背景已下载，已切换为默认视频背景", "Default video downloaded and applied", "기본 동영상을 받아 적용했어요");
        Lang.Add("默认视频背景下载完成，但切换失败，看日志喵", "Default video downloaded, but switching failed, check the log", "기본 동영상 받기 완료, 하지만 전환에 실패했어요. 로그를 확인해 주세요");
        Lang.Add("正在下载默认视频背景，稍等喵", "Downloading default video, please wait", "기본 동영상을 받는 중이에요, 잠시만 기다려 주세요");
        Lang.Add("正在下载或更新中，稍等再试喵", "A download or update is running, try again later", "다운로드/업데이트가 진행 중이에요, 잠시 후 다시 시도해 주세요");
        Lang.Add("下载默认视频背景失败，稍后再试喵", "Failed to download default video, try again later", "기본 동영상 받기 실패, 잠시 후 다시 시도해 주세요");
        Lang.Add("还没有可用的视频文件喵（点下面的「下载默认视频背景」，或用「选择视频文件」自己选一个）", "No video file available yet (tap \"Download default video\" below, or pick one with \"Choose video file\")", "아직 사용할 동영상이 없어요 (아래 \"기본 동영상 받기\"를 누르거나 \"동영상 파일 선택\"으로 직접 고르세요)");
        // ---- 静态背景 / 立绘 / 头像 ----
        Lang.Add("这个图片文件不存在喵", "This image file doesn't exist", "이 이미지 파일이 없어요");
        Lang.Add("背景图片保存失败喵", "Failed to save background image", "배경 이미지 저장 실패");
        Lang.Add("背景已更换喵", "Background changed", "배경이 변경되었어요");
        Lang.Add("背景图片加载失败喵", "Failed to load background image", "배경 이미지 불러오기 실패");
        Lang.Add("现在用的就是默认背景喵", "Already using the default background", "이미 기본 배경을 사용 중이에요");
        Lang.Add("恢复默认背景失败，看日志喵", "Failed to restore default background, check the log", "기본 배경 복원 실패, 로그를 확인해 주세요");
        Lang.Add("立绘保存失败喵", "Failed to save portrait", "스탠딩 이미지 저장 실패");
        Lang.Add("立绘图片读取失败喵", "Failed to load portrait image", "스탠딩 이미지 불러오기 실패");
        Lang.Add("立绘已更换喵", "Portrait changed", "스탠딩 이미지가 변경되었어요");
        // ---- 背景音乐 / 宠物模型 / 指令 ----
        Lang.Add("还没有设置背景音乐喵", "No background music set yet", "아직 배경 음악이 설정되지 않았어요");
        Lang.Add("这个谱面没有找到音乐文件喵", "No music file found for this chart", "이 채보에서 음악 파일을 찾지 못했어요");
        Lang.Add("模型包读不进来，换一个文件再试", "Can't read this model pack, try another file", "이 모델 팩을 읽을 수 없어요, 다른 파일로 시도해 주세요");
        Lang.Add("备份没成功，没敢删这个模型喵", "Backup failed, model not deleted", "백업에 실패해 이 모델을 삭제하지 않았어요");
        Lang.Add("删模型失败了，看日志喵", "Failed to delete model, check the log", "모델 삭제 실패, 로그를 확인해 주세요");
        Lang.Add("已支持：help / clear-save …（其他指令待定义）", "Supported: help / clear-save ... (more to come)", "지원됨: help / clear-save ... (추후 추가 예정)");
        Lang.Add("未识别的指令：", "Unknown command: ", "알 수 없는 명령어: ");
        Lang.Add("身份已被成就锁定：", "Identity locked by achievement: ", "업적에 의해 신분이 잠겼어요: ");
        Lang.Add("身份已设为：", "Identity set to: ", "신분이 설정되었어요: ");
        // ---- 库 / 导入 / 检验 / 评级 / 官谱 ----
        Lang.Add("导入失败：谱面没能保存到库目录", "Import failed: chart wasn't saved to the library", "가져오기 실패: 채보가 라이브러리에 저장되지 않았어요");
        Lang.Add("这个压缩包里没找到 .adofai 谱面", "No .adofai charts found in this archive", "이 압축 파일에서 .adofai 채보를 찾지 못했어요");
        Lang.Add("压缩包解压失败", "Failed to extract archive", "압축 파일 해제 실패");
        Lang.Add("这个谱面文件打不开（{0}）喵", "Can't open this chart file ({0})", "이 채보 파일을 열 수 없어요 ({0})");
        Lang.Add("这个谱面的文件好像不见了…菜单里的操作可能失败", "This chart's file seems missing... menu actions may fail", "이 채보의 파일이 없는 것 같아요... 메뉴 작업이 실패할 수 있어요");
        Lang.Add("这个谱面在原来的位置，文件没有删", "This chart stayed where it was, file not deleted", "이 채보는 원래 자리에 있어요, 파일은 삭제되지 않았어요");
        Lang.Add("还没有扫到可用的谱面喵", "No charts found yet", "아직 사용할 수 있는 채보가 없어요");
        Lang.Add("抽中的谱面读不到文件喵", "Can't read the picked chart file", "뽑힌 채보 파일을 읽을 수 없어요");
        Lang.Add("先选择一个谱面喵", "Pick a chart first", "먼저 채보를 선택해 주세요");
        Lang.Add("没有需要清理的记录", "No records to clean up", "정리할 기록이 없어요");
        Lang.Add("清理未执行：备份失败（详见日志）", "Cleanup skipped: backup failed (see log)", "정리가 실행되지 않았어요: 백업 실패 (로그 참조)");
        Lang.Add("通关记录已清空喵", "Clear records wiped", "클리어 기록이 비워졌어요");
        Lang.Add("游玩次数已清空喵", "Play counts wiped", "플레이 횟수가 비워졌어요");
        Lang.Add("评级完毕：没有可评级的谱面喵", "Rating done: no charts to rate", "등급 산정 완료: 매길 채보가 없어요");
        Lang.Add("现在连不上网喵（TUF 是网上查的），先检查一下网络", "No internet connection (TUF is queried online), check your network", "지금 인터넷에 연결할 수 없어요 (TUF는 온라인 조회예요), 네트워크를 확인해 주세요");
        Lang.Add("现在还不能进关卡（游戏还没就绪）", "Can't enter the level yet (game not ready)", "아직 레벨에 들어갈 수 없어요 (게임이 준비되지 않음)");
        Lang.Add("进入关卡失败，看日志喵", "Failed to enter level, check the log", "레벨 진입 실패, 로그를 확인해 주세요");
        Lang.Add("这个谱面没能自动打开（编辑器里是空的），用编辑器的打开功能再试一次喵", "This chart couldn't be opened automatically (editor is empty), try the editor's open function", "이 채보를 자동으로 열지 못했어요 (에디터가 비어 있음), 에디터의 열기 기능으로 다시 시도해 주세요");
        // ---- 更新器 ----
        Lang.Add("正在检查更新…", "Checking for updates...", "업데이트 확인 중...");
        Lang.Add("安装更新失败，看日志喵", "Update install failed, check the log", "업데이트 설치 실패, 로그를 확인해 주세요");
        Lang.Add("接口返回空内容喵", "Server returned empty content", "서버가 빈 내용을 반환했어요");
        // ---- 其他 ----
        Lang.Add("已导出到桌面：", "Exported to desktop: ", "바탕 화면으로 내보냈어요: ");
    }

    /// <summary>注册第十三批词条：官谱（官方关卡）详情区的存档数据/通关记录/寄录图整组
    /// 与成就弹窗、判定槽位词。配套改动：成就卡与官谱详情的显示处已补 Lang.Tr / TrFmt
    ///（名字词条本来就在表里，此前是显示处没过 Tr —— 详见功能逻辑表 3.5）。</summary>
    public static void Batch13() {
        // ---- 官谱详情：游戏存档数据（左栏）----
        Lang.Add("完成度：{0}%", "Completion: {0}%", "완료도: {0}%");
        Lang.Add("尝试次数：{0} 次", "Attempts: {0}", "시도 횟수: {0}회");
        Lang.Add("状态：", "Status: ", "상태: ");
        Lang.Add("完美通关", "Perfect clear", "퍼펙트 클리어");
        Lang.Add("已通关", "Cleared", "클리어함");
        Lang.Add("未通关", "Not cleared", "미클리어");
        Lang.Add("（读游戏存档失败：{0}）", "(Failed to read game save: {0})", "(게임 세이브 읽기 실패: {0})");
        Lang.Add("完成度：—\n尝试次数：—\n状态：游戏没给这个世界记数据", "Completion: —\nAttempts: —\nStatus: the game has no data for this world", "완료도: —\n시도 횟수: —\n상태: 이 월드에는 게임 기록이 없어요");
        // ---- 官谱详情：准确度与我们自己的统计（右栏）----
        Lang.Add("最佳准确度：{0}%", "Best accuracy: {0}%", "최고 정확도: {0}%");
        Lang.Add("最佳 X 准确度：{0}%", "Best X-accuracy: {0}%", "최고 X 정확도: {0}%");
        Lang.Add("最佳准确度：—", "Best accuracy: —", "최고 정확도: —");
        Lang.Add("最佳 X 准确度：—", "Best X-accuracy: —", "최고 X 정확도: —");
        Lang.Add("本关游玩 {0} 次 · 通关 {1} 次 · 严判 {2} 次", "Played {0} · Cleared {1} · Strict {2}", "이 레벨 {0}회 플레이 · 클리어 {1}회 · 엄격 {2}회");
        Lang.Add("（统计读取失败：{0}）", "(Failed to read stats: {0})", "(통계 읽기 실패: {0})");
        // ---- 官谱详情：通关记录列表 ----
        Lang.Add("这一关的游玩次数：{0}    通关 {1} 次   严判 {2} 次", "Plays: {0}    Clears: {1}    Strict: {2}", "플레이 횟수: {0}    클리어 {1}회    엄격 {2}회");
        Lang.Add("还没有通关记录。", "No clear records yet.", "아직 클리어 기록이 없어요.");
        Lang.Add("判定未知", "Unknown judge", "판정 알 수 없음");
        Lang.Add("完成度 {0}%", "Completion {0}%", "완료도 {0}%");
        // ---- 官谱详情：寄录图 ----
        Lang.Add("这一关共 {0} 次死亡", "{0} deaths on this level", "이 레벨에서 총 {0}회 사망");
        Lang.Add("还没有寄录数据。", "No death map data yet.", "아직 사망 지도 데이터가 없어요.");
        // ---- 宠物气泡 ----
        Lang.Add("今日游玩时间", "Played today", "오늘 플레이 시간");
    }
}
