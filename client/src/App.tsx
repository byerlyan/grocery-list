import { FormEvent, useEffect, useMemo, useRef, useState } from 'react'
import { authApi, categoryApi, groceryApi, householdApi } from './api'
import {
  type GroceryInput,
  type GroceryItem,
  type HouseholdCategory,
  type LoginInput,
  type RegisterInput,
  type Session,
} from './types'

type Filter = 'all' | 'active' | 'completed'
type AuthMode = 'login' | 'register'

const emptyDraft: GroceryInput = {
  name: '',
  quantity: 1,
  unit: '',
  category: 'Produce',
  isCompleted: false,
}

function App() {
  const [session, setSession] = useState<Session | null | undefined>(undefined)

  useEffect(() => {
    authApi.session().then(setSession).catch(() => setSession(null))
  }, [])

  if (session === undefined) {
    return <div className="app-loading"><span className="loading-dot" /> Opening Grocery List…</div>
  }

  if (session === null) return <AuthScreen onAuthenticated={setSession} />

  return <GroceryDashboard session={session} onSessionChange={setSession} onSignedOut={() => setSession(null)} />
}

function AuthScreen({ onAuthenticated }: { onAuthenticated: (session: Session) => void }) {
  const [mode, setMode] = useState<AuthMode>('login')
  const [login, setLogin] = useState<LoginInput>({ email: '', password: '', rememberMe: true })
  const [register, setRegister] = useState<RegisterInput>({ displayName: '', email: '', password: '', householdName: '' })
  const [error, setError] = useState('')
  const [isSubmitting, setIsSubmitting] = useState(false)

  async function submit(event: FormEvent) {
    event.preventDefault()
    setError('')
    setIsSubmitting(true)
    try {
      const session = mode === 'login' ? await authApi.login(login) : await authApi.register(register)
      onAuthenticated(session)
    } catch (reason) {
      setError((reason as Error).message)
    } finally {
      setIsSubmitting(false)
    }
  }

  function switchMode(nextMode: AuthMode) {
    setMode(nextMode)
    setError('')
  }

  return (
    <main className="auth-shell">
      <section className="auth-story">
        <a className="brand auth-brand" href="#top" aria-label="Grocery List home">
          <span className="brand-mark" aria-hidden="true">M</span>
          <span>Grocery List</span>
        </a>
        <div>
          <p className="eyebrow">Shop together</p>
          <h1>A shared list for<br /><em>everyday things.</em></h1>
          <p className="auth-intro">One place for the household to add, check, and remember what matters.</p>
        </div>
        <p className="auth-footnote">Private by default · Shared by invite</p>
      </section>

      <section className="auth-panel" aria-labelledby="auth-title">
        <div className="auth-card">
          <div className="auth-tabs" role="tablist" aria-label="Account access">
            <button type="button" role="tab" aria-selected={mode === 'login'} onClick={() => switchMode('login')}>Sign in</button>
            <button type="button" role="tab" aria-selected={mode === 'register'} onClick={() => switchMode('register')}>Create account</button>
          </div>

          <div className="auth-heading">
            <p className="eyebrow">{mode === 'login' ? 'Welcome back' : 'Start a household'}</p>
            <h2 id="auth-title">{mode === 'login' ? 'Your list is waiting.' : 'Make grocery day easier.'}</h2>
          </div>

          <form className="auth-form" onSubmit={submit}>
            {mode === 'register' && (
              <>
                <label className="light-field">
                  <span>Your name</span>
                  <input
                    value={register.displayName}
                    onChange={(event) => setRegister({ ...register, displayName: event.target.value })}
                    autoComplete="name"
                    placeholder="Sam"
                    required
                  />
                </label>
                <label className="light-field">
                  <span>Household name <i>optional</i></span>
                  <input
                    value={register.householdName}
                    onChange={(event) => setRegister({ ...register, householdName: event.target.value })}
                    placeholder="The Oak Street kitchen"
                    maxLength={80}
                  />
                </label>
              </>
            )}

            <label className="light-field">
              <span>Email</span>
              <input
                type="email"
                value={mode === 'login' ? login.email : register.email}
                onChange={(event) => mode === 'login'
                  ? setLogin({ ...login, email: event.target.value })
                  : setRegister({ ...register, email: event.target.value })}
                autoComplete="email"
                placeholder="you@example.com"
                required
              />
            </label>
            <label className="light-field">
              <span>Password</span>
              <input
                type="password"
                minLength={8}
                value={mode === 'login' ? login.password : register.password}
                onChange={(event) => mode === 'login'
                  ? setLogin({ ...login, password: event.target.value })
                  : setRegister({ ...register, password: event.target.value })}
                autoComplete={mode === 'login' ? 'current-password' : 'new-password'}
                placeholder={mode === 'login' ? 'Your password' : '8+ characters with a number'}
                required
              />
            </label>

            {mode === 'login' && (
              <label className="remember-option">
                <input
                  type="checkbox"
                  checked={login.rememberMe}
                  onChange={(event) => setLogin({ ...login, rememberMe: event.target.checked })}
                />
                Keep me signed in
              </label>
            )}

            {error && <p className="auth-error" role="alert">{error}</p>}
            <button className="primary-button" type="submit" disabled={isSubmitting}>
              <span>{isSubmitting ? 'Just a moment…' : mode === 'login' ? 'Sign in' : 'Create my list'}</span>
              <span aria-hidden="true">→</span>
            </button>
          </form>
        </div>
      </section>
    </main>
  )
}

function GroceryDashboard({
  session,
  onSessionChange,
  onSignedOut,
}: {
  session: Session
  onSessionChange: (session: Session) => void
  onSignedOut: () => void
}) {
  const [items, setItems] = useState<GroceryItem[]>([])
  const [categories, setCategories] = useState<HouseholdCategory[]>([])
  const [draft, setDraft] = useState<GroceryInput>(emptyDraft)
  const [filter, setFilter] = useState<Filter>('all')
  const [editingId, setEditingId] = useState<string | null>(null)
  const [isLoading, setIsLoading] = useState(true)
  const [isSaving, setIsSaving] = useState(false)
  const [error, setError] = useState('')
  const [inviteCode, setInviteCode] = useState('')
  const [shareStatus, setShareStatus] = useState('')
  const [isJoining, setIsJoining] = useState(false)
  const [isManagingCategories, setIsManagingCategories] = useState(false)
  const [categoryDraft, setCategoryDraft] = useState<{ id: string | null; name: string; icon: string }>({
    id: null,
    name: '',
    icon: '🏷️',
  })
  const [categoryError, setCategoryError] = useState('')
  const nameInput = useRef<HTMLInputElement>(null)

  useEffect(() => {
    loadItems()
  }, [])

  async function loadItems() {
    setIsLoading(true)
    try {
      const [nextItems, nextCategories] = await Promise.all([groceryApi.list(), categoryApi.list()])
      setItems(nextItems)
      setCategories(nextCategories)
      if (!nextCategories.some((category) => category.name === draft.category) && nextCategories[0]) {
        setDraft((current) => ({ ...current, category: nextCategories[0].name }))
      }
    } catch (reason) {
      setError((reason as Error).message)
    } finally {
      setIsLoading(false)
    }
  }

  const visibleItems = useMemo(() => {
    if (filter === 'active') return items.filter((item) => !item.isCompleted)
    if (filter === 'completed') return items.filter((item) => item.isCompleted)
    return items
  }, [filter, items])

  const remaining = items.filter((item) => !item.isCompleted).length
  const completed = items.length - remaining
  const progress = items.length ? Math.round((completed / items.length) * 100) : 0

  function categoryIcon(categoryName: string) {
    return categories.find((category) => category.name === categoryName)?.icon ?? '🛒'
  }

  async function submitItem(event: FormEvent) {
    event.preventDefault()
    if (!draft.name.trim()) {
      setError('Add a name for your item.')
      nameInput.current?.focus()
      return
    }

    setIsSaving(true)
    setError('')
    try {
      if (editingId) {
        const updated = await groceryApi.update(editingId, draft)
        setItems((current) => current.map((item) => (item.id === editingId ? updated : item)))
      } else {
        const created = await groceryApi.create(draft)
        setItems((current) => [...current, created])
      }
      resetForm()
    } catch (reason) {
      setError((reason as Error).message)
    } finally {
      setIsSaving(false)
    }
  }

  function resetForm() {
    setDraft(emptyDraft)
    setEditingId(null)
    requestAnimationFrame(() => nameInput.current?.focus())
  }

  function beginEdit(item: GroceryItem) {
    setDraft({
      name: item.name,
      quantity: item.quantity,
      unit: item.unit,
      category: item.category,
      isCompleted: item.isCompleted,
    })
    setEditingId(item.id)
    setError('')
    nameInput.current?.focus()
  }

  async function toggleItem(item: GroceryItem) {
    const optimistic = { ...item, isCompleted: !item.isCompleted }
    setItems((current) => current.map((entry) => (entry.id === item.id ? optimistic : entry)))
    setError('')
    try {
      const updated = await groceryApi.update(item.id, optimistic)
      setItems((current) => current.map((entry) => (entry.id === item.id ? updated : entry)))
    } catch (reason) {
      setItems((current) => current.map((entry) => (entry.id === item.id ? item : entry)))
      setError((reason as Error).message)
    }
  }

  async function removeItem(id: string) {
    const previous = items
    setItems((current) => current.filter((item) => item.id !== id))
    setError('')
    try {
      await groceryApi.remove(id)
      if (editingId === id) resetForm()
    } catch (reason) {
      setItems(previous)
      setError((reason as Error).message)
    }
  }

  async function clearCompleted() {
    const previous = items
    setItems((current) => current.filter((item) => !item.isCompleted))
    setError('')
    try {
      await groceryApi.clearCompleted()
    } catch (reason) {
      setItems(previous)
      setError((reason as Error).message)
    }
  }

  async function copyInviteCode() {
    try {
      await navigator.clipboard.writeText(session.household.inviteCode)
      setShareStatus('Copied')
    } catch {
      setShareStatus('Select and copy the code')
    }
    window.setTimeout(() => setShareStatus(''), 2000)
  }

  async function rotateInviteCode() {
    setShareStatus('')
    try {
      const result = await householdApi.rotateInviteCode()
      onSessionChange({ ...session, household: { ...session.household, inviteCode: result.inviteCode } })
      setShareStatus('New code ready')
    } catch (reason) {
      setShareStatus((reason as Error).message)
    }
  }

  async function joinHousehold(event: FormEvent) {
    event.preventDefault()
    if (!inviteCode.trim()) return
    setIsJoining(true)
    setShareStatus('')
    try {
      const nextSession = await householdApi.join(inviteCode)
      onSessionChange(nextSession)
      setInviteCode('')
      const [nextItems, nextCategories] = await Promise.all([groceryApi.list(), categoryApi.list()])
      setItems(nextItems)
      setCategories(nextCategories)
      if (nextCategories[0]) setDraft({ ...emptyDraft, category: nextCategories[0].name })
      setShareStatus(`Joined ${nextSession.household.name}`)
    } catch (reason) {
      setShareStatus((reason as Error).message)
    } finally {
      setIsJoining(false)
    }
  }

  function beginCategoryEdit(category: HouseholdCategory) {
    setCategoryDraft({ id: category.id, name: category.name, icon: category.icon })
    setCategoryError('')
    setIsManagingCategories(true)
  }

  function resetCategoryDraft() {
    setCategoryDraft({ id: null, name: '', icon: '🏷️' })
    setCategoryError('')
  }

  async function saveCategory(event: FormEvent) {
    event.preventDefault()
    setCategoryError('')
    try {
      if (categoryDraft.id) {
        const previous = categories.find((category) => category.id === categoryDraft.id)
        const updated = await categoryApi.update(categoryDraft.id, categoryDraft)
        setCategories((current) => current.map((category) => category.id === updated.id ? updated : category))
        if (previous && previous.name !== updated.name) {
          setItems((current) => current.map((item) =>
            item.category === previous.name ? { ...item, category: updated.name } : item))
          setDraft((current) => current.category === previous.name
            ? { ...current, category: updated.name }
            : current)
        }
      } else {
        const created = await categoryApi.create(categoryDraft)
        setCategories((current) => [...current, created])
      }
      resetCategoryDraft()
    } catch (reason) {
      setCategoryError((reason as Error).message)
    }
  }

  async function removeCategory(category: HouseholdCategory) {
    if (!window.confirm(`Remove “${category.name}”? Items in it will be moved to another category.`)) return
    setCategoryError('')
    try {
      const result = await categoryApi.remove(category.id)
      setCategories((current) => current.filter((value) => value.id !== category.id))
      setItems((current) => current.map((item) =>
        item.category === category.name ? { ...item, category: result.reassignedTo } : item))
      setDraft((current) => current.category === category.name
        ? { ...current, category: result.reassignedTo }
        : current)
      resetCategoryDraft()
    } catch (reason) {
      setCategoryError((reason as Error).message)
    }
  }

  async function signOut() {
    try {
      await authApi.logout()
    } finally {
      onSignedOut()
    }
  }

  return (
    <main className="page-shell">
      <header className="masthead">
        <a className="brand" href="#top" aria-label="Grocery List home">
          <span className="brand-mark" aria-hidden="true">G</span>
          <span>Grocery List</span>
        </a>
        <div className="user-nav">
          <span>Hi, {session.user.displayName}</span>
          <button type="button" onClick={signOut}>Sign out</button>
        </div>
      </header>

      <section className="hero" id="top">
        <div>
          <p className="eyebrow">{session.household.name}</p>
          <h1>Everything you need.<br /><em>Nothing you don’t.</em></h1>
        </div>
        <div className="progress-card" aria-label={`${completed} of ${items.length} items collected`}>
          <div className="progress-copy">
            <span>{items.length ? `${completed} of ${items.length}` : 'Fresh start'}</span>
            <strong>{items.length ? `${progress}%` : '—'}</strong>
          </div>
          <div className="progress-track"><span style={{ width: `${progress}%` }} /></div>
          <small>{remaining === 1 ? '1 item left to find' : `${remaining} items left to find`}</small>
        </div>
      </section>

      <section className="workspace">
        <aside className="sidebar">
          <section className="household-card" aria-labelledby="household-title">
            <div className="household-heading">
              <div>
                <p className="eyebrow">Shared household</p>
                <h2 id="household-title">{session.household.name}</h2>
              </div>
              <div className="member-stack" aria-label={`${session.household.members.length} members`}>
                {session.household.members.slice(0, 3).map((member) => (
                  <span key={member.id} title={member.displayName}>{member.displayName.slice(0, 1).toUpperCase()}</span>
                ))}
              </div>
            </div>
            <p className="household-help">Share this code with someone you trust. They can join and update this list.</p>
            <div className="invite-code-row">
              <code>{session.household.inviteCode}</code>
              <button type="button" onClick={copyInviteCode}>Copy</button>
              <button type="button" onClick={rotateInviteCode} aria-label="Create a new invite code">↻</button>
            </div>
            <form className="join-form" onSubmit={joinHousehold}>
              <input
                value={inviteCode}
                onChange={(event) => setInviteCode(event.target.value.toUpperCase())}
                placeholder="Enter another invite code"
                maxLength={8}
                aria-label="Household invite code"
              />
              <button type="submit" disabled={isJoining}>{isJoining ? '…' : 'Join'}</button>
            </form>
            {shareStatus && <p className="share-status" role="status">{shareStatus}</p>}
            <button
              className="manage-categories-button"
              type="button"
              aria-expanded={isManagingCategories}
              onClick={() => setIsManagingCategories((current) => !current)}
            >
              <span>Household categories</span>
              <span aria-hidden="true">{isManagingCategories ? '−' : '+'}</span>
            </button>
            {isManagingCategories && (
              <div className="category-manager">
                <div className="managed-category-list" aria-label="Household categories">
                  {categories.map((category) => (
                    <button
                      key={category.id}
                      type="button"
                      className={categoryDraft.id === category.id ? 'active' : ''}
                      onClick={() => beginCategoryEdit(category)}
                    >
                      <span aria-hidden="true">{category.icon}</span>
                      {category.name}
                    </button>
                  ))}
                </div>
                <form className="category-editor" onSubmit={saveCategory}>
                  <input
                    className="category-icon-input"
                    value={categoryDraft.icon}
                    onChange={(event) => setCategoryDraft({ ...categoryDraft, icon: event.target.value })}
                    aria-label="Category icon"
                    maxLength={16}
                    required
                  />
                  <input
                    value={categoryDraft.name}
                    onChange={(event) => setCategoryDraft({ ...categoryDraft, name: event.target.value })}
                    placeholder="Category name"
                    aria-label="Category name"
                    maxLength={32}
                    required
                  />
                  <button type="submit">{categoryDraft.id ? 'Save' : 'Add'}</button>
                </form>
                {categoryDraft.id && (
                  <div className="category-editor-actions">
                    <button type="button" onClick={resetCategoryDraft}>Add another</button>
                    <button
                      type="button"
                      className="category-remove-button"
                      onClick={() => {
                        const category = categories.find((value) => value.id === categoryDraft.id)
                        if (category) removeCategory(category)
                      }}
                    >
                      Remove category
                    </button>
                  </div>
                )}
                {categoryError && <p className="category-error" role="alert">{categoryError}</p>}
                <p className="category-note">Changes appear for everyone in this household.</p>
              </div>
            )}
          </section>

          <form className="add-card" onSubmit={submitItem}>
            <div className="form-heading">
              <span>{editingId ? 'Edit item' : 'Add an item'}</span>
              <span className="form-number">01</span>
            </div>

            <label className="field field-name">
              <span>What do you need?</span>
              <input
                ref={nameInput}
                value={draft.name}
                onChange={(event) => setDraft({ ...draft, name: event.target.value })}
                placeholder="e.g. Heirloom tomatoes"
                maxLength={80}
                autoComplete="off"
              />
            </label>

            <div className="form-row">
              <label className="field quantity-field">
                <span>Quantity</span>
                <input
                  type="number"
                  min="1"
                  max="999"
                  value={draft.quantity}
                  onChange={(event) => setDraft({ ...draft, quantity: Number(event.target.value) })}
                />
              </label>
              <label className="field">
                <span>Unit <i>optional</i></span>
                <input
                  value={draft.unit}
                  onChange={(event) => setDraft({ ...draft, unit: event.target.value })}
                  placeholder="bags, lbs…"
                  maxLength={24}
                />
              </label>
            </div>

            <fieldset className="category-picker">
              <legend>Category</legend>
              <div className="category-grid">
                {categories.map((category) => (
                  <label key={category.id} className={draft.category === category.name ? 'category-option selected' : 'category-option'}>
                    <input
                      type="radio"
                      name="category"
                      value={category.name}
                      checked={draft.category === category.name}
                      onChange={() => setDraft({ ...draft, category: category.name })}
                    />
                    <span aria-hidden="true">{category.icon}</span>
                    {category.name}
                  </label>
                ))}
              </div>
            </fieldset>

            {error && <p className="error-message" role="alert">{error}</p>}

            <div className="form-actions">
              {editingId && <button className="text-button" type="button" onClick={resetForm}>Cancel</button>}
              <button className="add-button" type="submit" disabled={isSaving}>
                <span>{isSaving ? 'Saving…' : editingId ? 'Save changes' : 'Add to list'}</span>
                <span aria-hidden="true">{editingId ? '✓' : '+'}</span>
              </button>
            </div>
          </form>
        </aside>

        <section className="list-panel" aria-labelledby="list-title">
          <div className="list-header">
            <div>
              <p className="eyebrow">This week</p>
              <h2 id="list-title">Your Grocery list</h2>
            </div>
            <span className="item-count">{items.length.toString().padStart(2, '0')} items</span>
          </div>

          <div className="list-toolbar">
            <div className="filters" aria-label="Filter list">
              {(['all', 'active', 'completed'] as Filter[]).map((option) => (
                <button
                  key={option}
                  className={filter === option ? 'active' : ''}
                  type="button"
                  onClick={() => setFilter(option)}
                >
                  {option[0].toUpperCase() + option.slice(1)}
                </button>
              ))}
            </div>
            {completed > 0 && <button className="clear-button" type="button" onClick={clearCompleted}>Clear checked</button>}
          </div>

          <div className="grocery-list" aria-live="polite">
            {isLoading ? (
              <div className="empty-state"><span className="loading-dot" /> Loading your list…</div>
            ) : visibleItems.length === 0 ? (
              <div className="empty-state">
                <span className="empty-icon" aria-hidden="true">{filter === 'completed' ? '✓' : '✦'}</span>
                <h3>{items.length ? `No ${filter} items` : 'Your basket is empty'}</h3>
                <p>{items.length ? 'Try another view.' : 'Add your first item to start the week.'}</p>
              </div>
            ) : (
              visibleItems.map((item, index) => (
                <article className={item.isCompleted ? 'grocery-item completed' : 'grocery-item'} key={item.id}>
                  <button
                    className="check-button"
                    type="button"
                    aria-label={item.isCompleted ? `Mark ${item.name} as not collected` : `Mark ${item.name} as collected`}
                    aria-pressed={item.isCompleted}
                    onClick={() => toggleItem(item)}
                  >
                    {item.isCompleted && '✓'}
                  </button>
                  <span className="category-icon" aria-hidden="true">{categoryIcon(item.category)}</span>
                  <div className="item-copy">
                    <h3>{item.name}</h3>
                    <p>{item.category} · {item.quantity}{item.unit ? ` ${item.unit}` : ''}</p>
                  </div>
                  <span className="list-index">{(index + 1).toString().padStart(2, '0')}</span>
                  <div className="item-actions">
                    <button type="button" onClick={() => beginEdit(item)} aria-label={`Edit ${item.name}`}>Edit</button>
                    <button type="button" onClick={() => removeItem(item.id)} aria-label={`Remove ${item.name}`}>Remove</button>
                  </div>
                </article>
              ))
            )}
          </div>
        </section>
      </section>

      <footer>
        <span>Grocery List</span>
        <span>Shop thoughtfully · Waste less</span>
      </footer>
    </main>
  )
}

export default App
