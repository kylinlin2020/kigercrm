import type { ApiId, PageResult } from '../../types'
import type {
  CrmShipmentCommandResultDto,
  CrmShipmentCreateDto,
  CrmShipmentDetailDto,
  CrmShipmentLineDto,
  CrmShipmentLinePageQueryDto,
  CrmShipmentListItemDto,
  CrmShipmentPageQueryDto,
  CrmShipmentStatusUpdateDto,
} from './shipment.types'
import { createDynamicApiClient } from '../../base'

const shipmentQueryApi = createDynamicApiClient('CrmShipmentQuery')
const shipmentCommandApi = createDynamicApiClient('CrmShipment')

export const crmShipmentApi = {
  create(input: CrmShipmentCreateDto) {
    return shipmentCommandApi.post<CrmShipmentCommandResultDto, CrmShipmentCreateDto>('Create', input)
  },
  changeStatus(input: CrmShipmentStatusUpdateDto) {
    return shipmentCommandApi.post<CrmShipmentCommandResultDto, CrmShipmentStatusUpdateDto>('ChangeStatus', input)
  },
  page(input: CrmShipmentPageQueryDto) {
    return shipmentQueryApi.post<PageResult<CrmShipmentListItemDto>, CrmShipmentPageQueryDto>('Page', input)
  },
  detail(id: ApiId) {
    return shipmentQueryApi.get<CrmShipmentDetailDto | null>('Detail', { id })
  },
  lines(input: CrmShipmentLinePageQueryDto) {
    return shipmentQueryApi.post<PageResult<CrmShipmentLineDto>, CrmShipmentLinePageQueryDto>('LinesPage', input)
  },
}
