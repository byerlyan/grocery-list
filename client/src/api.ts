import type { CategoryInput, GroceryInput, GroceryItem, HouseholdCategory, LoginInput, RegisterInput, Session } from './types'

const endpoint = '/api/groceries'

async function request<T>(url: string, options?: RequestInit): Promise<T> {
  const response = await fetch(url, {
    headers: { 'Content-Type': 'application/json' },
    credentials: 'include',
    ...options,
  })

  if (!response.ok) {
    const details = await response.json().catch(() => null) as { message?: string } | null
    if (response.status === 401) throw new Error('Your email or password is incorrect.')
    throw new Error(details?.message ?? 'Something went wrong. Please try again.')
  }

  if (response.status === 204) return undefined as T
  return response.json() as Promise<T>
}

export const groceryApi = {
  list: () => request<GroceryItem[]>(endpoint),
  create: (item: GroceryInput) =>
    request<GroceryItem>(endpoint, { method: 'POST', body: JSON.stringify(item) }),
  update: (id: string, item: GroceryInput) =>
    request<GroceryItem>(`${endpoint}/${id}`, { method: 'PUT', body: JSON.stringify(item) }),
  remove: (id: string) => request<void>(`${endpoint}/${id}`, { method: 'DELETE' }),
  clearCompleted: () => request<{ removed: number }>(`${endpoint}/completed`, { method: 'DELETE' }),
}

export const categoryApi = {
  list: () => request<HouseholdCategory[]>('/api/categories'),
  create: (input: CategoryInput) =>
    request<HouseholdCategory>('/api/categories', { method: 'POST', body: JSON.stringify(input) }),
  update: (id: string, input: CategoryInput) =>
    request<HouseholdCategory>(`/api/categories/${id}`, { method: 'PUT', body: JSON.stringify(input) }),
  remove: (id: string) =>
    request<{ reassignedTo: string }>(`/api/categories/${id}`, { method: 'DELETE' }),
}

export const authApi = {
  session: () => request<Session>('/api/auth/me'),
  register: (input: RegisterInput) =>
    request<Session>('/api/auth/register', { method: 'POST', body: JSON.stringify(input) }),
  login: (input: LoginInput) =>
    request<Session>('/api/auth/login', { method: 'POST', body: JSON.stringify(input) }),
  logout: () => request<void>('/api/auth/logout', { method: 'POST' }),
}

export const householdApi = {
  join: (inviteCode: string) =>
    request<Session>('/api/household/join', { method: 'POST', body: JSON.stringify({ inviteCode }) }),
  rotateInviteCode: () =>
    request<{ inviteCode: string }>('/api/household/invite-code', { method: 'POST' }),
}
