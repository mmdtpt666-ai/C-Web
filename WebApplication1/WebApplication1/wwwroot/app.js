/**
 * 映刻浏览器端入口：通过本地 Vue 全局发行版运行，不依赖 npm 构建。
 * 会话由服务端 HttpOnly Cookie 管理；浏览器存储仅保存收藏和观看记录。
 */
const { createApp, nextTick } = Vue;
createApp({
  // 认证状态、目录筛选和界面反馈分别保存在当前 Vue 实例中。
  data: () => ({ booting: true, user: null, registering: false, showPassword: false, busy: false,
    form: { email: '', password: '', name: '' }, authMessage: '', authSuccess: false, csrf: '',
    videos: [], loadingVideos: false, feedError: '', query: '', search: '', category: '全部', section: '首页',
    categories: ['全部', '生活', '旅行', '科技', '音乐', '美食', '学习'],
    navItems: [{name:'首页',icon:'⌂'},{name:'探索发现',icon:'◈'}],
    saved: [], history: [], sidebarOpen: false, accountOpen: false, activeVideo: null, toast: '', toastTimer: null }),
  computed: {
    /**
     * 根据分类、搜索词及当前导航计算可见目录，不修改原始视频集合。
     * @returns {Array<Object>} 符合所有筛选条件的视频。
     */
    filteredVideos() {
      return this.videos.filter(v => (this.category === '全部' || v.category === this.category)
        && (!this.search || (v.title + v.channel).toLowerCase().includes(this.search.toLowerCase()))
        && (this.section !== '稍后观看' || this.saved.includes(v.id))
        && (this.section !== '观看记录' || this.history.includes(v.id)));
    }
  },
  /**
   * 首次挂载时恢复服务端登录态，再读取当前身份对应的 CSRF 令牌。
   * @returns {Promise<void>} 初始化结束后解除页面加载状态。
   */
  async mounted() {
    document.addEventListener('keydown', this.onKey);
    try {
      const response = await fetch('/api/auth/me', {cache:'no-store'});
      if (response.ok) { this.user = await response.json(); this.restoreCollections(); await this.loadVideos(); }
      else if (response.status !== 401) this.authMessage = '服务暂不可用，请稍后重试。';
      await this.refreshCsrf();
    } catch { this.authMessage = '无法连接服务，请检查网络后刷新。'; }
    finally { this.booting = false; }
  },
  /** 卸载前释放全局事件和提示计时器，避免残留回调。 */
  beforeUnmount() { document.removeEventListener('keydown', this.onKey); clearTimeout(this.toastTimer); },
  methods: {
    /**
     * 处理页面级退出快捷键。
     * @param {KeyboardEvent} event 浏览器键盘事件。
     */
    onKey(event) { if (event.key === 'Escape') { this.activeVideo = null; this.accountOpen = false; } },
    /**
     * 获取与当前 Cookie 身份关联的令牌；登录或退出后必须重新调用。
     * @returns {Promise<void>} 令牌更新完成时结束。
     * @throws {Error} 网络异常或服务端无法签发令牌时抛出。
     */
    async refreshCsrf() { const response = await fetch('/api/auth/csrf', {cache:'no-store'}); if (!response.ok) throw Error('无法获取页面验证信息，请刷新重试。'); this.csrf = (await response.json()).token; },
    /**
     * 发送同源 JSON 写请求，统一附加 CSRF 令牌并转换业务错误。
     * @param {string} path 当前站点的 API 路径。
     * @param {Object} [body] 请求数据，未提供时发送空对象。
     * @returns {Promise<Object>} 服务端返回的 JSON 数据。
     * @throws {Error} 网络失败、令牌获取失败或非成功响应时抛出。
     */
    async post(path, body) {
      if (!this.csrf) await this.refreshCsrf();
      const response = await fetch(path, { method: 'POST', headers: {'Content-Type':'application/json','X-CSRF-TOKEN':this.csrf}, body:JSON.stringify(body ?? {}) });
      const data = await response.json().catch(() => ({}));
      if (!response.ok) throw Error(data.message || (response.status === 429 ? '操作太频繁，请一分钟后再试。' : '请求失败，请稍后重试。'));
      return data;
    },
    /** 切换注册与登录表单，清空密码和上一次请求提示。 */
    toggleAuth() { this.registering = !this.registering; this.authMessage=''; this.authSuccess=false; this.form.password=''; },
    /**
     * 提交认证表单；注册成功后停留在登录表单，登录成功后加载目录。
     * 使用 busy 阻止重复提交，并将失败原因展示在当前表单中。
     * @returns {Promise<void>} 请求与必要的界面状态更新结束时完成。
     */
    async submitAuth() {
      if (this.busy) return;
      this.busy=true; this.authMessage=''; this.authSuccess=false;
      try {
        const data=await this.post('/api/auth/'+(this.registering?'register':'login'),this.form);
        this.form.password='';
        if (this.registering) { this.registering=false; this.authSuccess=true; this.authMessage=data.message; }
        else { this.user=data; this.restoreCollections(); await this.refreshCsrf(); await this.loadVideos(); }
      } catch(error) { this.authMessage=error.message; }
      finally { this.busy=false; }
    },
    /**
     * 读取受保护的视频目录；会话失效时回到登录页，其他失败允许重试。
     * @returns {Promise<void>} 完成目录加载或错误提示更新。
     */
    async loadVideos() {
      this.loadingVideos=true; this.feedError='';
      try {
        const response=await fetch('/api/videos');
        if (response.status===401) { this.user=null; await this.refreshCsrf(); return; }
        if (!response.ok) throw Error('视频加载失败，请重试。');
        this.videos=await response.json();
      } catch(error) { this.feedError=error.message; }
      finally { this.loadingVideos=false; }
    },
    /**
     * 服务端退出成功后清空当前用户界面，并获取匿名身份的 CSRF 令牌。
     * @returns {Promise<void>} 退出流程结束时完成，失败原因通过提示展示。
     */
    async logout() {
      this.busy=true;
      try { await this.post('/api/auth/logout'); this.user=null; this.videos=[]; this.activeVideo=null; this.accountOpen=false; this.section='首页'; this.search=''; this.query=''; this.category='全部'; await this.refreshCsrf(); }
      catch(error) { this.notify(error.message); }
      finally { this.busy=false; }
    },
    /**
     * 按当前邮箱恢复本浏览器的收藏和历史记录，只接受整数视频 ID。
     * 存储损坏或不可访问时使用空集合，不影响服务端登录态。
     */
    restoreCollections() {
      for (const key of ['saved','history']) {
        try { const value=JSON.parse(localStorage.getItem(`yink:${this.user.email}:${key}`) || '[]'); this[key]=Array.isArray(value)?value.filter(Number.isInteger):[]; }
        catch { this[key]=[]; }
      }
    },
    /**
     * 保存当前账号的浏览器集合；隐私模式或空间不足时保留内存状态。
     * @param {'saved'|'history'} key 需要保存的实例集合名称。
     */
    persist(key) { try { localStorage.setItem(`yink:${this.user.email}:${key}`,JSON.stringify(this[key])); } catch { this.notify('浏览器存储不可用，本次记录仅保留到刷新。'); } },
    /**
     * 切换视频的稍后观看状态并保存到当前浏览器。
     * @param {number} id 视频目录主键。
     */
    toggleSaved(id) { this.saved=this.saved.includes(id)?this.saved.filter(x=>x!==id):[...this.saved,id]; this.persist('saved'); },
    /**
     * 打开放映室并将视频置于历史记录首位，DOM 更新后聚焦关闭按钮。
     * @param {Object|undefined} video 视频元数据；未提供时忽略本次操作。
     * @returns {Promise<void>} 完成界面更新和焦点移动。
     */
    async play(video) { if (!video) return; this.activeVideo=video; this.history=[video.id,...this.history.filter(x=>x!==video.id)]; this.persist('history'); await nextTick(); this.$refs.closePlayer?.focus(); },
    /**
     * 展示短时消息，新消息覆盖旧计时器，避免提示过早消失。
     * @param {string} message 展示给用户的纯文本消息。
     */
    notify(message) { this.toast=message; clearTimeout(this.toastTimer); this.toastTimer=setTimeout(()=>this.toast='',4500); }
  }
}).mount('#app');
