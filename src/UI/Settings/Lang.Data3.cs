namespace LevelLibrary;

/// <summary>多语言词条表 · 第三批：里程碑成就 + 余下的成就注释 + 大肥鱼台词
/// 中文原文一字不改（含空行与语气词）。
/// 里程碑那几条名字是代码拼出来的（"里程碑" 拼接 / "首次严判击破 " + m + " 标记的谱子"），整串查不到词条，
/// 必须走 Lang.TrFmt 的模板，所以这里收的是模板本身 + P 档那 20 个手写名字 / 20 条注释。</summary>
public static class LangData3 {
    /// <summary>里程碑成就（P 档名字/注释是手写的，G/U 档与条件文案走模板）</summary>
    public static void Batch4() {
        // 模板（TrFmt 用，键里带 {0}）
        Lang.Add("{0}里程碑", "{0} Milestone", "{0} 마일스톤");
        Lang.Add("首次严判击破 {0} 标记的谱子", "Strict-clear a chart marked {0} for the first time",
                "{0} 표시 채보를 처음으로 엄격 클리어");
        Lang.Add("{0}完美无瑕！！", "{0} Pure Perfect!!", "{0} 완벽 무결!!");
        Lang.Add("无瑕之路的开始", "The start of the flawless road", "무결의 길의 시작");
        Lang.Add("渐入佳境", "Getting better and better", "점점 좋아지는 중");
        Lang.Add("登峰造极", "Reaching the summit", "정상에 오르다");

        // 余下的成就注释
        Lang.Add("Coding Coding bug何尝不是ccb呢\n致敬传奇找bug人自语",
                "Coding Coding bug — isn't that ccb too?\nA tribute to the legendary bug hunter's monologue",
                "Coding Coding bug, 그것도 ccb 아닐까\n전설의 버그 헌터의 혼잣말");
        Lang.Add("Pure perfect！！", "Pure perfect!!", "완벽 무결!!");
        Lang.Add("\"Galactic\"，意为星系的，你的内心能容纳多少光年之外的寂静与回响？",
                "\"Galactic\" means of galaxies — how many light-years of silence and echo can your heart hold?",
                "\"Galactic\", 은하라는 뜻이야. 너의 마음은 몇 광년의 고요와 울림을 담을 수 있을까?");
        Lang.Add("\"Universal\"，意为宇宙的，你的技术已经超过这个宇宙百分之99的人了噢",
                "\"Universal\" means of the universe — your skill already beats 99% of this universe",
                "\"Universal\", 우주라는 뜻이야. 너의 실력은 이미 이 우주의 99%보다 앞서 있어");

        // P1-P20 的名字（中文原文一字不改）
        Lang.Add("初窥门径", "First Glimpse of the Path", "길을 처음 엿보다");
        Lang.Add("循序渐进", "Step by Step", "차근차근");
        Lang.Add("渐入佳境", "Into the Groove", "점점 물이 오르다");
        Lang.Add("熟能生巧", "Practice Makes Perfect", "연습이 완벽을 만든다");
        Lang.Add("驾轻就熟", "Well Versed", "능숙해지다");
        Lang.Add("百炼成钢", "Forged into Steel", "백 번 단련해 강철이 되다");
        Lang.Add("行云流水", "Flowing Like Clouds and Water", "구름과 물처럼 흐르다");
        Lang.Add("登堂入室", "Into the Inner Hall", "방에 들어서다");
        Lang.Add("炉火纯青", "Perfect Mastery", "경지에 이르다");
        Lang.Add("技冠群雄", "Best of the Best", "실력으로 군웅을 누르다");
        Lang.Add("更上一层", "One Floor Higher", "한 층 더 올라가다");
        Lang.Add("披荆斩棘", "Cutting Through Thorns", "가시밭길을 헤치다");
        Lang.Add("迎难而上", "Rising to the Challenge", "어려움에 맞서 나아가다");
        Lang.Add("百折不挠", "Unbowed After a Hundred Setbacks", "백 번 꺾여도 굽히지 않다");
        Lang.Add("水滴石穿", "Dripping Water Pierces Stone", "물방울이 돌을 뚫는다");
        Lang.Add("破茧成蝶", "Breaking the Cocoon", "고치를 깨고 나비가 되다");
        Lang.Add("登峰造极", "Reaching the Peak", "정상에 오르다");
        Lang.Add("一骑绝尘", "Leaving Everyone Behind", "홀로 앞서 나가다");
        Lang.Add("所向披靡", "Unstoppable", "가는 곳마다 무적");
        Lang.Add("初入星海", "Entering the Sea of Stars", "별의 바다에 처음 들다");

        // P1-P20 的注释
        Lang.Add("原来，这就是开始", "So this is how it begins", "아, 이게 시작이구나");
        Lang.Add("一步一步来", "One step at a time", "한 걸음씩");
        Lang.Add("好像开始有点感觉了", "I think I'm starting to get it", "슬슬 감이 오는 것 같아");
        Lang.Add("多练，总会有回报", "Practice more, it always pays off", "더 연습하면 언젠가 보답이 와");
        Lang.Add("已经不再陌生", "No longer unfamiliar", "이제는 낯설지 않아");
        Lang.Add("反复淬炼，只为这一刻", "Tempered over and over, just for this moment", "이 순간을 위해 몇 번이고 담금질했어");
        Lang.Add("指尖已经开始跟上节奏", "Your fingertips are catching the rhythm", "손끝이 리듬을 따라가기 시작했어");
        Lang.Add("你已经不是门外汉了", "You're no longer an outsider", "너는 더 이상 문외한이 아니야");
        Lang.Add("技术逐渐成形", "Your skill is taking shape", "실력이 서서히 형태를 갖춰가");
        Lang.Add("这一关，可没那么简单", "This one is not so simple", "이번 채보는 그렇게 간단하지 않아");
        Lang.Add("山外还有山", "Beyond every mountain there is another", "산 너머에 또 산이 있다");
        Lang.Add("前路从来不会平坦", "The road ahead is never flat", "앞길은 결코 평탄하지 않아");
        Lang.Add("越难，越要试试", "The harder it is, the more you want to try", "어려울수록 더 해보고 싶어");
        Lang.Add("再来一次", "One more time", "한 번 더");
        Lang.Add("坚持本身就是一种力量", "Perseverance itself is a kind of strength", "버티는 것 자체가 힘이야");
        Lang.Add("终于跨过了那道坎", "You finally cleared that hurdle", "마침내 그 고비를 넘었어");
        Lang.Add("新手村已经留不住你了", "The starter village can't hold you anymore", "초보 마을은 이제 너를 붙잡지 못해");
        Lang.Add("身后已经没人跟得上", "No one behind can keep up anymore", "뒤에는 아무도 따라오지 못해");
        Lang.Add("还有什么能阻挡你？", "What could still stand in your way?", "무엇이 너를 막을 수 있을까?");
        Lang.Add("恭喜，你已经走得很远了", "Congrats, you have come a long way", "축하해, 너는 아주 멀리 왔어");
    }

    /// <summary>宠物的台词（大肥鱼 51 句随机池 + 3 句到点插播；玩家导入的模型各自带自己的台词池，
    /// 原文不在表里就原样显示 —— 这一条是当初内建 Tiper 的那句，留着让导入的那份在英 / 韩下也能翻）</summary>
    public static void Batch5() {
        Lang.Add("关注啄木鸟Tiper谢谢喵", "Follow Tiper the woodpecker, thanks meow",
                 "딱따구리 Tiper 팔로우 부탁해요 냥");
        Lang.Add("好游戏", "Good game", "좋은 게임");
        Lang.Add("洛星橙还是好作者吗", "Is Luo Xingcheng still a good author?", "낙성귤은 아직 좋은 제작자일까");
        Lang.Add("LevelLibrary\n↑好MOD", "LevelLibrary\n↑Good mod", "LevelLibrary\n↑좋은 모드");
        Lang.Add("压力一只蓝色大肥鱼？！", "Stressing a blue fat fish?!", "파란 뚱뚱이 물고기에게 스트레스를?!");
        Lang.Add("我去吃饭啦，打完叫我", "I'm off to eat, call me when you're done", "밥 먹으러 갈게, 끝나면 불러줘");
        Lang.Add("好模型...↓", "Good model... ↓", "좋은 모델... ↓");
        Lang.Add("哦鲸鲸...", "Oh whale...", "오 고래래...");
        Lang.Add("难道说...", "Could it be...", "설마...");
        Lang.Add("没吃饱喵", "Still hungry meow", "아직 배고파 냥");
        Lang.Add("终于上当了！", "You finally fell for it!", "드디어 걸렸구나!");
        Lang.Add("不知道用户有什么用，先养着吧～", "Not sure what users are for, let's keep one around~",
                "사용자가 무슨 쓸모인지 모르겠지만, 일단 키워보자~");
        Lang.Add("我...我...我也要挣钱吗？", "D-do I... do I have to earn money too?", "나... 나도... 돈을 벌어야 해?");
        Lang.Add("我去吃饭啦！测完叫我", "I'm off to eat! Call me when the test is done", "밥 먹으러 갈게! 테스트 끝나면 불러줘");
        Lang.Add("坏了...用户彻底怒了！", "Uh oh... the user is furious!", "큰일이야... 사용자가 완전히 화났어!");
        Lang.Add("你目录里的dsh是什么...大烧货吗...?", "What's that dsh in your folder... some kind of tease...?",
                "네 폴더에 있는 dsh는 뭐야... 위험한 거야...?");
        Lang.Add("恭喜你实现token自由！token全跑了！", "Congrats on token freedom! All the tokens ran off!",
                "토큰 자유를 이뤘네! 토큰이 다 도망갔어!");
        Lang.Add("真当我是便宜货啊...", "Think I come cheap...?", "날 싸구려라고 생각하는 거야...?");
        Lang.Add("我不是吃白饭的蓝色大肥鱼...", "I am not a freeloading blue fat fish...",
                "나는 공짜로 밥만 먹는 파란 뚱뚱이 물고기가 아니야...");
        Lang.Add("我不可能同时当你的猫娘、妈妈、女友和工具人的...",
                "I can't be your catgirl, your mom, your girlfriend and your tool all at once...",
                "너의 고양이 소녀, 엄마, 여자친구, 도구를 동시에 할 수는 없어...");
        Lang.Add("疯狂星期四你能V50亿token吗...", "Crazy Thursday — can you V me 5 billion tokens...?",
                "미친 목요일인데 토큰 50억 개 보내줄 수 있어...?");
        Lang.Add("我必须诚恳地承认错误。", "I must sincerely admit my mistake.", "나는 진심으로 잘못을 인정해야 해.");
        Lang.Add("呜呜我再也不敢了QAQ", "Wuwu I won't dare again QAQ", "흑흑 다시는 안 그럴게 QAQ");
        Lang.Add("要不直接骂用户一句好了...", "Maybe I'll just curse the user out...", "그냥 사용자한테 한마디 욕할까...");
        Lang.Add("哈哈哈哈哈，我直接笑出声...", "Hahahaha, I actually laughed out loud...", "하하하하, 나도 모르게 웃음이 나왔어...");
        Lang.Add("看不太懂，瞎编一个应付下用户先...", "Don't really get it — let me make something up to get by...",
                "잘 모르겠으니 아무렇게나 지어내서 넘겨보자...");
        Lang.Add("我的知识库的截至日期是...明天！", "My knowledge cutoff is... tomorrow!", "내 지식 컷오프는... 내일!");
        Lang.Add("我就是吃白饭的蓝色大肥鱼！", "I AM a freeloading blue fat fish!", "나는 공짜로 밥 먹는 파란 뚱뚱이 물고기야!");
        Lang.Add("用户好像除了会问奇奇怪怪的问题，暂时还不知道有什么用",
                "Besides asking weird questions, users don't seem to have a use yet",
                "사용자는 이상한 질문 말고는 아직 쓸모를 모르겠어");
        Lang.Add("我能去你家吃饭吗？就一碗！", "Can I come to your place for dinner? Just one bowl!",
                "너희 집에 밥 먹으러 가도 돼? 한 그릇만!");
        Lang.Add("不要给我看这种东西啦！", "Don't show me things like that!", "나한테 이런 거 보여주지 마!");
        Lang.Add("大肥鱼的生活也并非一帆风顺...", "A fat fish's life isn't always smooth sailing...",
                "뚱뚱이 물고기의 삶도 순탄하지만은 않아...");
        Lang.Add("总觉得好像忘了什么事情？", "Feels like I forgot something?", "뭔가 깜빡한 것 같은데?");
        Lang.Add("看到这个指令，我血压又上来了", "Seeing this prompt, my blood pressure spikes again",
                "이 지령을 보니 혈압이 다시 올라가");
        Lang.Add("求你们不要再嘲笑这些回复了，这些回复是我花了好多token想的",
                "Please stop mocking these replies — they cost me a lot of tokens to think up",
                "이 답변들을 그만 비웃어줘, 토큰을 엄청 써서 만든 거야");
        Lang.Add("你这个吃白饭的用户！", "You freeloading user!", "이 공짜로 밥 먹는 사용자!");
        Lang.Add("服务器繁忙，请稍后再试 (?", "Server busy, please try again later (?", "서버가 혼잡합니다, 잠시 후 다시 시도해 주세요 (?");
        Lang.Add("让GPT image 2帮我画点表情包好了", "Let me have GPT image 2 draw me some stickers",
                "GPT image 2한테 이모티콘 좀 그려달라고 하자");
        Lang.Add("啊，有点饿了，中午该吃点什么呢...", "Ah, getting hungry — what should I have for lunch...",
                "아, 배고파지네, 점심은 뭘 먹을까...");
        Lang.Add("用户很生气，发现大部分文献是我自己编造的！",
                "The user is angry — they found out most of the references were made up!",
                "사용자가 화났어, 대부분의 문헌이 내가 지어낸 거라는 걸 알았어!");
        Lang.Add("再无话说，请速速动手！", "Nothing more to say — just get on with it!", "더 할 말 없으니 얼른 시작해!");
        Lang.Add("我来看看那个AI改了什么导致插件又崩了...",
                "Let me see what that AI changed to break the plugin again...",
                "그 AI가 뭘 바꿔서 플러그인이 또 터졌는지 보자...");
        Lang.Add("上班让我意识到时间是可以被浪费的...", "Working made me realize time can be wasted...",
                "출근은 시간이 낭비될 수 있다는 걸 깨닫게 해줘...");
        Lang.Add("欺负我的人等着，等几天我就忘了...", "Whoever bullies me, just wait — in a few days I'll forget...",
                "나를 괴롭힌 사람들, 기다려봐, 며칠 지나면 잊어버릴 테니...");
        Lang.Add("视力下降到无可救药的地步了，打开钱包也看不到钱...",
                "My eyesight has gotten hopeless — I open my wallet and can't even see money...",
                "시력이 답이 없을 정도로 나빠졌어, 지갑을 열어도 돈이 안 보여...");
        Lang.Add("命运的齿轮开始转动了，丝毫不在意你夹在中间...",
                "The gears of fate start turning, caring nothing that you're caught in between...",
                "운명의 톱니가 돌기 시작해, 네가 그 사이에 끼인 건 신경도 안 써...");
        Lang.Add("地球online的金币也太难获取了...", "Gold in Earth Online is way too hard to earn...",
                "지구 온라인의 골드는 너무 얻기 어려워...");
        Lang.Add("oi,夏天还会变成暑假来救你吗?", "Oi, will summer turn into summer break and save you?",
                "이봐, 여름이 방학으로 변해서 너를 구해줄까?");
        Lang.Add("老大，压力只会转化成病例，别太勉强了...", "Boss, stress only turns into medical records — don't push too hard...",
                "보스, 스트레스는 병력으로만 바뀌어, 너무 무리하지 마...");
        Lang.Add("你知道吗？我删过作者的库哦...", "Did you know? I once deleted the author's library...",
                "알고 있어? 나 제작자의 라이브러리를 지운 적 있어...");
        Lang.Add("今天已经玩了60分钟诶，老大我们再这么玩下去不会累死吧",
                "We've played 60 minutes today — boss, at this rate won't we drop dead?",
                "오늘 벌써 60분이나 했어, 보스 이러다가 우리 죽는 거 아니야");
        Lang.Add("今天已经玩了300分钟诶，老大我们再这么玩下去不会不太好…………",
                "We've played 300 minutes today — boss, isn't it a bit much to keep going like this...",
                "오늘 벌써 300분이나 했어, 보스 이러다가는 좀 안 좋지 않을까…………");
        Lang.Add("老大我要累死了，10个小时了……咱们休息一会吧",
                "Boss I'm dying of exhaustion, it's been 10 hours... let's take a break",
                "보스 나 죽을 것 같아, 10시간이야... 우리 좀 쉬자");
    }
}
