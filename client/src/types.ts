export type GroceryItem = {
  id: string
  name: string
  quantity: number
  unit: string
  category: string
  isCompleted: boolean
  createdAt: string
}

export type GroceryInput = Omit<GroceryItem, 'id' | 'createdAt'>

export type HouseholdCategory = {
  id: string
  name: string
  icon: string
  sortOrder: number
}

export type CategoryInput = Pick<HouseholdCategory, 'name' | 'icon'>

export type HouseholdMember = {
  id: string
  displayName: string
}

export type Session = {
  user: {
    id: string
    displayName: string
    email: string
  }
  household: {
    id: string
    name: string
    inviteCode: string
    members: HouseholdMember[]
  }
}

export type RegisterInput = {
  displayName: string
  email: string
  password: string
  householdName: string
}

export type LoginInput = {
  email: string
  password: string
  rememberMe: boolean
}
